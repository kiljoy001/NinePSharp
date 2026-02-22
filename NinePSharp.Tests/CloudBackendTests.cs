using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Backends.Cloud;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

public class CloudBackendTests
{
    private readonly Mock<ILuxVaultService> _vaultMock = new();
    private readonly Mock<IAmazonS3> _s3Mock = new();
    private readonly Mock<IAmazonSecretsManager> _secretsMock = new();
    private readonly AwsBackendConfig _config = new() { Name = "AWS", MountPath = "/aws" };

    [Fact]
    public async Task AwsS3_ListBuckets_ReturnsDirectoryListing()
    {
        // Arrange
        _s3Mock.Setup(x => x.ListBucketsAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ListBucketsResponse
               {
                   Buckets = new List<S3Bucket>
                   {
                       new S3Bucket { BucketName = "bucket1" },
                       new S3Bucket { BucketName = "bucket2" }
                   }
               });

        var fs = new AwsS3FileSystem(_config, _s3Mock.Object, _vaultMock.Object);
        
        // Act: Read root directory
        var read = await fs.ReadAsync(new Tread(1, 0, 0, 8192));
        var entries = ParseDirectory(read.Data.ToArray());

        // Assert
        Assert.Contains(entries, s => s.Name == "bucket1");
        Assert.Contains(entries, s => s.Name == "bucket2");
        Assert.All(entries, s => Assert.Equal(QidType.QTDIR, s.Qid.Type));
    }

    [Fact]
    public async Task AwsS3_ReadObject_ReturnsContent()
    {
        // Arrange
        var contentString = "Hello S3";
        var contentStream = new MemoryStream(Encoding.UTF8.GetBytes(contentString));
        
        _s3Mock.Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new GetObjectResponse
               {
                   ResponseStream = contentStream,
                   HttpStatusCode = System.Net.HttpStatusCode.OK
               });

        var fs = new AwsS3FileSystem(_config, _s3Mock.Object, _vaultMock.Object);
        // Simulate walking to /bucket/key
        var walkedFs = (AwsS3FileSystem)fs.Clone(); 
        await walkedFs.WalkAsync(new Twalk(1, 0, 1, new[] { "my-bucket" }));
        await walkedFs.WalkAsync(new Twalk(1, 1, 2, new[] { "my-key.txt" }));

        // Act
        var read = await walkedFs.ReadAsync(new Tread(1, 0, 0, 8192));
        var result = Encoding.UTF8.GetString(read.Data.ToArray());

        // Assert
        Assert.Equal(contentString, result);
    }

    [Fact]
    public async Task AwsSecrets_ListSecrets_ReturnsNames()
    {
        // Arrange
        _secretsMock.Setup(x => x.ListSecretsAsync(It.IsAny<ListSecretsRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ListSecretsResponse
                    {
                        SecretList = new List<SecretListEntry>
                        {
                            new SecretListEntry { Name = "prod/db" },
                            new SecretListEntry { Name = "dev/api" }
                        }
                    });

        var fs = new AwsSecretsFileSystem(_config, _secretsMock.Object, _vaultMock.Object);

        // Act
        var read = await fs.ReadAsync(new Tread(1, 0, 0, 8192));
        var content = Encoding.UTF8.GetString(read.Data.ToArray());

        // Assert
        Assert.Contains("prod/db", content);
        Assert.Contains("dev/api", content);
    }

    [Fact]
    public async Task AwsSecrets_GetSecretValue_ReturnsString()
    {
        // Arrange
        var secretValue = "super-secret-value";
        _secretsMock.Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GetSecretValueResponse
                    {
                        SecretString = secretValue
                    });

        var fs = new AwsSecretsFileSystem(_config, _secretsMock.Object, _vaultMock.Object);
        var walkedFs = (AwsSecretsFileSystem)fs.Clone();
        await walkedFs.WalkAsync(new Twalk(1, 0, 1, new[] { "prod/db" }));

        // Act
        var read = await walkedFs.ReadAsync(new Tread(1, 0, 0, 8192));
        var result = Encoding.UTF8.GetString(read.Data.ToArray());

        // Assert
        Assert.Equal(secretValue, result);
    }

    private static List<Stat> ParseDirectory(byte[] data)
    {
        var stats = new List<Stat>();
        int offset = 0;
        while (offset < data.Length)
        {
            stats.Add(new Stat(data, ref offset));
        }

        return stats;
    }
}
