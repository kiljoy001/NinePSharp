using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

[Collection("Cluster Manager Runtime")]
public sealed class ClusterManagerAdversarialPropertyFuzzTests
{
    [Property(MaxTest = 25)]
    public bool RegisterMountAsync_Normalizes_Path_And_Publishes_To_Snapshot(NonEmptyString rawPath)
    {
        string safe = NormalizeMountPath(rawPath.Get);
        var sut = CreateManager();
        try
        {
            sut.Start();
            sut.RegisterMountAsync(safe, () => new TaggedFileSystem("tagged")).Sync();

            var mounts = sut.GetRemoteMountPathsAsync().Sync();
            return mounts.Contains("/" + safe.TrimStart('/'));
        }
        finally
        {
            sut.StopAsync().Sync();
            sut.Dispose();
        }
    }

    [Fact]
    public async Task TryCreateRemoteFileSystemAsync_Returns_Fresh_Sessions_Per_Call()
    {
        int counter = 0;
        var sut = CreateManager();

        try
        {
            sut.Start();
            await sut.RegisterMountAsync("/fresh", () => new TaggedFileSystem("session-" + ++counter));

            var first = await sut.TryCreateRemoteFileSystemAsync("/fresh");
            var second = await sut.TryCreateRemoteFileSystemAsync("/fresh");

            first.Should().NotBeNull();
            second.Should().NotBeNull();
            first.Should().NotBeSameAs(second);

            var firstRead = await first!.ReadAsync(new Tread(1, 0, 0, 64));
            var secondRead = await second!.ReadAsync(new Tread(2, 0, 0, 64));

            Encoding.UTF8.GetString(firstRead.Data.ToArray()).Should().Be("session-1");
            Encoding.UTF8.GetString(secondRead.Data.ToArray()).Should().Be("session-2");
        }
        finally
        {
            await sut.StopAsync();
            sut.Dispose();
        }
    }

    [Property(MaxTest = 25)]
    public bool TryCreateRemoteFileSystemAsync_Unknown_Mount_Returns_Null(NonEmptyString registeredRaw, NonEmptyString missingRaw)
    {
        string registered = NormalizeMountPath(registeredRaw.Get);
        string missing = NormalizeMountPath(missingRaw.Get);
        if (registered == missing)
        {
            missing += "x";
        }

        var sut = CreateManager();
        try
        {
            sut.Start();
            sut.RegisterMountAsync(registered, () => new TaggedFileSystem("live")).Sync();
            return sut.TryCreateRemoteFileSystemAsync(missing).Sync() == null;
        }
        finally
        {
            sut.StopAsync().Sync();
            sut.Dispose();
        }
    }

    private static ClusterManager CreateManager()
    {
        string systemName = "ClusterAdv" + Guid.NewGuid().ToString("N")[..8];
        var config = new AkkaConfig
        {
            SystemName = systemName,
            Hostname = "127.0.0.1",
            Port = 0,
            Role = "backend"
        };

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(NullLogger.Instance);

        return new ClusterManager(
            NullLogger<ClusterManager>.Instance,
            loggerFactory.Object,
            config);
    }

    private static string NormalizeMountPath(string raw)
    {
        var chars = raw.Where(c => char.IsLetterOrDigit(c) || c == '/' || c == '_' || c == '-')
            .Take(20)
            .ToArray();
        string cleaned = new string(chars).Trim('/');
        return string.IsNullOrWhiteSpace(cleaned) ? "/mount" : "/" + cleaned;
    }

    private sealed class TaggedFileSystem : INinePFileSystem
    {
        private readonly byte[] _payload;

        public TaggedFileSystem(string tag)
        {
            _payload = Encoding.UTF8.GetBytes(tag);
        }

        public NinePDialect Dialect { get; set; }

        public Task<Rwalk> WalkAsync(Twalk twalk) => Task.FromResult(new Rwalk(twalk.Tag, Array.Empty<Qid>()));
        public Task<Ropen> OpenAsync(Topen topen) => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 1), 0));
        public Task<Rread> ReadAsync(Tread tread) => Task.FromResult(new Rread(tread.Tag, _payload));
        public Task<Rwrite> WriteAsync(Twrite twrite) => Task.FromResult(new Rwrite(twrite.Tag, twrite.Count));
        public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
        public Task<Rstat> StatAsync(Tstat tstat) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0, 0, 0, 0, "tag", "u", "g", "m")));
        public Task<Rwstat> WstatAsync(Twstat twstat) => Task.FromResult(new Rwstat(twstat.Tag));
        public Task<Rremove> RemoveAsync(Tremove tremove) => Task.FromResult(new Rremove(tremove.Tag));
        public INinePFileSystem Clone() => new TaggedFileSystem(Encoding.UTF8.GetString(_payload)) { Dialect = Dialect };
    }
}
