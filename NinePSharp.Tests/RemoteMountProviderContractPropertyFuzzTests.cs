using System;
using System.Linq;
using System.Text;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server;
using Xunit;

namespace NinePSharp.Tests;

public sealed class RemoteMountProviderContractPropertyFuzzTests
{
    [Property(MaxTest = 40)]
    public bool NullRemoteMountProvider_Is_Safe_For_Unknown_And_Registered_Mounts(NonEmptyString rawPath)
    {
        string path = NormalizeMountPath(rawPath.Get);
        using IRemoteMountProvider sut = new NullRemoteMountProvider();

        sut.Start();
        sut.RegisterMountAsync(path, () => new TaggedFileSystem("ignored")).Sync();

        var mounts = sut.GetRemoteMountPathsAsync().Sync();
        var resolved = sut.TryCreateRemoteFileSystemAsync(path).Sync();
        sut.StopAsync().Sync();

        return mounts.Count == 0 && resolved == null;
    }

    [Property(MaxTest = 30)]
    public bool AkkaRemoteMountProvider_Unknown_Mount_Returns_Null(NonEmptyString rawPath)
    {
        string path = NormalizeMountPath(rawPath.Get);
        using var sut = CreateAkkaProvider();

        try
        {
            sut.Start();
            return sut.TryCreateRemoteFileSystemAsync(path).Sync() == null;
        }
        finally
        {
            sut.StopAsync().Sync();
        }
    }

    [Fact]
    public void AkkaRemoteMountProvider_Register_Then_Create_Yields_Fresh_Sessions_Through_Interface()
    {
        using IRemoteMountProvider sut = CreateAkkaProvider();
        int counter = 0;

        try
        {
            sut.Start();
            sut.RegisterMountAsync("/contract", () => new TaggedFileSystem("session-" + ++counter)).Sync();

            var mounts = sut.GetRemoteMountPathsAsync().Sync();
            var first = sut.TryCreateRemoteFileSystemAsync("/contract").Sync();
            var second = sut.TryCreateRemoteFileSystemAsync("/contract").Sync();

            mounts.Should().Contain("/contract");
            first.Should().NotBeNull();
            second.Should().NotBeNull();
            first.Should().NotBeSameAs(second);

            var firstRead = first!.ReadAsync(new Tread(1, 0, 0, 64)).Sync();
            var secondRead = second!.ReadAsync(new Tread(2, 0, 0, 64)).Sync();

            Encoding.UTF8.GetString(firstRead.Data.ToArray()).Should().Be("session-1");
            Encoding.UTF8.GetString(secondRead.Data.ToArray()).Should().Be("session-2");
        }
        finally
        {
            sut.StopAsync().Sync();
        }
    }

    private static IRemoteMountProvider CreateAkkaProvider()
    {
        string systemName = "ProviderContract" + Guid.NewGuid().ToString("N")[..8];
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(NullLogger.Instance);

        return new ClusterManager(
            NullLogger<ClusterManager>.Instance,
            loggerFactory.Object,
            new AkkaConfig
            {
                SystemName = systemName,
                Hostname = "127.0.0.1",
                Port = 0,
                Role = "backend"
            });
    }

    private static string NormalizeMountPath(string raw)
    {
        var chars = raw.Where(c => char.IsLetterOrDigit(c) || c == '/' || c == '_' || c == '-')
            .Take(24)
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
