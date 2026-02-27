using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Coyote.SystematicTesting;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Server.Backends.Cloud;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;
using CoyoteTask = Microsoft.Coyote.Rewriting.Types.Threading.Tasks.Task;

namespace NinePSharp.Tests;

public class CloudBackendPropertyFuzzCoyoteTests
{
    private static AwsBackendConfig DefaultConfig => new() { Name = "AWS", MountPath = "/aws" };

    [Property(MaxTest = 45)]
    public bool AwsS3_Readdir_Pagination_RoundTrip_Property(PositiveInt bucketSeed, PositiveInt pageSeed)
    {
        int bucketCount = Math.Clamp(bucketSeed.Get % 20 + 1, 1, 20);
        uint pageSize = (uint)Math.Clamp(pageSeed.Get % 512 + 32, 32, 512);

        var s3 = new Mock<IAmazonS3>(MockBehavior.Strict);
        s3.Setup(x => x.ListBucketsAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new ListBucketsResponse
            {
                Buckets = Enumerable.Range(0, bucketCount)
                    .Select(i => new S3Bucket { BucketName = $"b{i:00}" })
                    .ToList()
            });

        var fs = new AwsS3FileSystem(DefaultConfig, s3.Object, new Mock<ILuxVaultService>().Object);

        var full = fs.ReadAsync(new Tread(1, 0, 0, ushort.MaxValue)).Sync();
        byte[] fullBytes = full.Data.ToArray();

        var reconstructed = new List<byte>();
        ulong offset = 0;
        int guard = 0;
        while (guard++ < 1024)
        {
            var page = fs.ReadAsync(new Tread(2, 0, offset, pageSize)).Sync();
            byte[] bytes = page.Data.ToArray();
            if (bytes.Length == 0)
            {
                break;
            }

            reconstructed.AddRange(bytes);
            offset += (ulong)bytes.Length;
        }

        return reconstructed.SequenceEqual(fullBytes);
    }

    [Property(MaxTest = 40)]
    public bool AwsCloud_Clone_Delegation_Does_Not_CrossTalk_Property(NonEmptyString bucketNameSeed, NonEmptyString secretNameSeed)
    {
        string bucket = Sanitize(bucketNameSeed.Get, "bucket");
        string secret = Sanitize(secretNameSeed.Get, "secret");

        var s3 = new Mock<IAmazonS3>(MockBehavior.Strict);
        s3.Setup(x => x.ListBucketsAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new ListBucketsResponse { Buckets = new List<S3Bucket> { new() { BucketName = bucket } } });

        var secrets = new Mock<IAmazonSecretsManager>(MockBehavior.Strict);
        secrets.Setup(x => x.ListSecretsAsync(It.IsAny<ListSecretsRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new ListSecretsResponse { SecretList = new List<SecretListEntry> { new() { Name = secret } } });

        var root = new AwsCloudFileSystem(DefaultConfig, s3.Object, secrets.Object, new Mock<ILuxVaultService>().Object);
        var s3View = (AwsCloudFileSystem)root.Clone();
        var secretsView = (AwsCloudFileSystem)root.Clone();

        s3View.WalkAsync(new Twalk(1, 1, 1, new[] { "s3" })).Sync();
        secretsView.WalkAsync(new Twalk(2, 1, 1, new[] { "secrets" })).Sync();

        var s3Read = s3View.ReadAsync(new Tread(3, 1, 0, 8192)).Sync();
        var secretRead = secretsView.ReadAsync(new Tread(4, 1, 0, 8192)).Sync();
        var rootRead = root.ReadAsync(new Tread(5, 1, 0, 128)).Sync();

        var s3Names = ParseStatNames(s3Read.Data.Span);
        string secretsText = Encoding.UTF8.GetString(secretRead.Data.Span);
        string rootText = Encoding.UTF8.GetString(rootRead.Data.Span);

        return s3Names.Contains(bucket, StringComparer.Ordinal)
            && secretsText.Contains(secret, StringComparison.Ordinal)
            && rootText.Contains("s3/", StringComparison.Ordinal)
            && rootText.Contains("secrets/", StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task Fuzz_AwsSecrets_Operation_Sequences_Do_Not_Throw_Unexpected_Runtime_Exceptions()
    {
        var random = new Random(0x51C2);
        var secrets = new Mock<IAmazonSecretsManager>(MockBehavior.Strict);
        secrets.Setup(x => x.ListSecretsAsync(It.IsAny<ListSecretsRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new ListSecretsResponse
            {
                SecretList = new List<SecretListEntry>
                {
                    new() { Name = "alpha" },
                    new() { Name = "beta" }
                }
            });
        secrets.Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync((GetSecretValueRequest req, System.Threading.CancellationToken _) =>
                new GetSecretValueResponse { SecretString = "value-for-" + (req.SecretId ?? "none") });
        secrets.Setup(x => x.PutSecretValueAsync(It.IsAny<PutSecretValueRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new PutSecretValueResponse());

        var fs = new AwsSecretsFileSystem(DefaultConfig, secrets.Object, new Mock<ILuxVaultService>().Object);

        for (int i = 0; i < 300; i++)
        {
            try
            {
                switch (random.Next(0, 6))
                {
                    case 0:
                    {
                        string[] path = RandomPath(random);
                        if (path.Length > 0)
                        {
                            await fs.WalkAsync(new Twalk((ushort)(10 + i), 1, 1, path));
                        }
                        break;
                    }
                    case 1:
                    {
                        await fs.ReadAsync(new Tread((ushort)(20 + i), 1, (ulong)random.Next(0, 256), (uint)random.Next(1, 256)));
                        break;
                    }
                    case 2:
                    {
                        byte[] data = new byte[random.Next(0, 96)];
                        random.NextBytes(data);
                        await fs.WriteAsync(new Twrite((ushort)(30 + i), 1, 0, data));
                        break;
                    }
                    case 3:
                    {
                        await fs.StatAsync(new Tstat((ushort)(40 + i), 1));
                        break;
                    }
                    case 4:
                    {
                        await fs.OpenAsync(new Topen((ushort)(50 + i), 1, (byte)random.Next(0, 4)));
                        break;
                    }
                    default:
                    {
                        await fs.ClunkAsync(new Tclunk((ushort)(60 + i), 1));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Assert.True(ex is NinePProtocolException,
                    $"Unexpected exception type during fuzz iteration {i}: {ex.GetType().Name}");
            }
        }
    }

    [Fact]
    public void Coyote_AwsCloud_Concurrent_SubFileSystems_Do_Not_CrossTalk()
    {
        var configuration = Microsoft.Coyote.Configuration.Create()
            .WithTestingIterations(180)
            .WithPartiallyControlledConcurrencyAllowed(true);

        var engine = TestingEngine.Create(configuration, async () =>
        {
            var s3 = new Mock<IAmazonS3>(MockBehavior.Strict);
            s3.Setup(x => x.ListBucketsAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new ListBucketsResponse
                {
                    Buckets = new List<S3Bucket> { new() { BucketName = "bucket-a" } }
                });

            var secrets = new Mock<IAmazonSecretsManager>(MockBehavior.Strict);
            secrets.Setup(x => x.ListSecretsAsync(It.IsAny<ListSecretsRequest>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new ListSecretsResponse
                {
                    SecretList = new List<SecretListEntry> { new() { Name = "secret-a" } }
                });

            var root = new AwsCloudFileSystem(DefaultConfig, s3.Object, secrets.Object, new Mock<ILuxVaultService>().Object);
            var s3View = (AwsCloudFileSystem)root.Clone();
            var secretsView = (AwsCloudFileSystem)root.Clone();

            var s3Task = CoyoteTask.Run(async () =>
            {
                await s3View.WalkAsync(new Twalk(1, 1, 1, new[] { "s3" }));
                await CoyoteTask.Yield();
                var read = await s3View.ReadAsync(new Tread(2, 1, 0, 4096));
                return string.Join('\n', ParseStatNames(read.Data.Span));
            });

            var secretTask = CoyoteTask.Run(async () =>
            {
                await CoyoteTask.Yield();
                await secretsView.WalkAsync(new Twalk(3, 1, 1, new[] { "secrets" }));
                var read = await secretsView.ReadAsync(new Tread(4, 1, 0, 4096));
                return Encoding.UTF8.GetString(read.Data.Span);
            });

            var outcomes = await CoyoteTask.WhenAll(s3Task, secretTask);
            string s3Text = outcomes[0];
            string secretsText = outcomes[1];

            if (!s3Text.Contains("bucket-a", StringComparison.Ordinal))
            {
                throw new Exception("S3 clone did not return expected bucket listing.");
            }

            if (!secretsText.Contains("secret-a", StringComparison.Ordinal))
            {
                throw new Exception("Secrets clone did not return expected secret listing.");
            }
        });

        engine.Run();
        Assert.True(engine.TestReport.NumOfFoundBugs == 0, engine.TestReport.GetText(configuration));
    }

    private static string[] RandomPath(Random random)
    {
        string[] atoms =
        {
            "..",
            "alpha",
            "beta",
            "secrets",
            "s3",
            "missing",
            "",
            "   "
        };

        int len = random.Next(0, 4);
        var path = new string[len];
        for (int i = 0; i < len; i++)
        {
            path[i] = atoms[random.Next(0, atoms.Length)];
        }

        return path;
    }

    private static IReadOnlyList<string> ParseStatNames(ReadOnlySpan<byte> bytes)
    {
        var names = new List<string>();
        int offset = 0;

        while (offset + 2 <= bytes.Length)
        {
            int start = offset;
            try
            {
                var stat = new Stat(bytes, ref offset);
                if (offset <= start)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(stat.Name))
                {
                    names.Add(stat.Name);
                }
            }
            catch
            {
                break;
            }
        }

        return names;
    }

    private static string Sanitize(string raw, string fallback)
    {
        var filtered = new string(raw.Where(char.IsLetterOrDigit).Take(20).ToArray());
        return string.IsNullOrWhiteSpace(filtered) ? fallback : filtered.ToLowerInvariant();
    }
}
