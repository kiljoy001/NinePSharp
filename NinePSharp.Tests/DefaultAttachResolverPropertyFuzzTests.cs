using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public sealed class DefaultAttachResolverPropertyFuzzTests
{
    [Property(MaxTest = 60)]
    public bool ResolveAsync_LocalBackend_Wins_Over_Remote_For_Equivalent_Targets(NonEmptyString rawName, PositiveInt selector)
    {
        string safe = NormalizeName(rawName.Get);
        string aname = (selector.Get % 3) switch
        {
            0 => safe,
            1 => "/" + safe,
            _ => "svc_" + safe
        };

        var localFs = new MarkerFileSystem("local");
        var remoteFs = new MarkerFileSystem("remote");
        var backend = new Mock<IProtocolBackend>(MockBehavior.Strict);
        backend.SetupGet(b => b.MountPath).Returns("/" + safe);
        backend.SetupGet(b => b.Name).Returns("svc_" + safe);
        backend.Setup(b => b.GetFileSystem(It.IsAny<SecureString?>(), It.IsAny<X509Certificate2?>()))
            .Returns(localFs);

        var cluster = new RecordingClusterManager
        {
            RemoteFactory = _ => Task.FromResult<INinePFileSystem?>(remoteFs)
        };

        var sut = new DefaultAttachResolver(new[] { backend.Object }, cluster);
        var resolution = sut.ResolveAsync(aname, null, null).Sync();
        var fs = resolution.Target!.CreateSession();

        return !resolution.IsRoot &&
               !resolution.Target.IsRemote &&
               ReferenceEquals(localFs, fs) &&
               cluster.RequestedMounts.Count == 0;
    }

    [Property(MaxTest = 60)]
    public bool ResolveAsync_RemoteLookup_Normalizes_Leading_Slash(NonEmptyString rawName)
    {
        string safe = NormalizeName(rawName.Get);
        var cluster = new RecordingClusterManager
        {
            RemoteFactory = path => Task.FromResult<INinePFileSystem?>(new MarkerFileSystem("remote:" + path))
        };

        var sut = new DefaultAttachResolver(Array.Empty<IProtocolBackend>(), cluster);
        var resolution = sut.ResolveAsync(safe, null, null).Sync();

        return !resolution.IsRoot &&
               resolution.Target != null &&
               resolution.Target.IsRemote &&
               resolution.Target.MountPath == "/" + safe &&
               cluster.RequestedMounts.SequenceEqual(new[] { "/" + safe });
    }

    [Property(MaxTest = 40)]
    public bool ResolveAsync_RemoteSessions_Are_Fresh_Per_Call(NonEmptyString rawName)
    {
        string safe = NormalizeName(rawName.Get);
        int counter = 0;
        var cluster = new RecordingClusterManager
        {
            RemoteFactory = path => Task.FromResult<INinePFileSystem?>(new MarkerFileSystem($"{path}:{++counter}"))
        };

        var sut = new DefaultAttachResolver(Array.Empty<IProtocolBackend>(), cluster);
        var first = sut.ResolveAsync("/" + safe, null, null).Sync();
        var second = sut.ResolveAsync("/" + safe, null, null).Sync();
        var firstFs = sut.TryCreateRemoteFileSystemAsync(first.Target!.MountPath).Sync();
        var secondFs = sut.TryCreateRemoteFileSystemAsync(second.Target!.MountPath).Sync();

        return firstFs is MarkerFileSystem a &&
               secondFs is MarkerFileSystem b &&
               a.Id != b.Id &&
               !ReferenceEquals(a, b);
    }

    [Property(MaxTest = 50)]
    public bool GetRootMounts_Skips_Blank_Paths_And_PrototypeClone_Returns_Fresh_FileSystems(NonEmptyString rawName, bool includeBlank)
    {
        string safe = NormalizeName(rawName.Get);
        int cloneCount = 0;

        var validBackend = new Mock<IProtocolBackend>(MockBehavior.Strict);
        validBackend.SetupGet(b => b.MountPath).Returns("/" + safe);
        validBackend.SetupGet(b => b.Name).Returns("valid");
        validBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2?>()))
            .Returns(() => new MarkerFileSystem("clone-" + ++cloneCount));

        var blankBackend = new Mock<IProtocolBackend>(MockBehavior.Strict);
        blankBackend.SetupGet(b => b.MountPath).Returns(includeBlank ? " " : string.Empty);
        blankBackend.SetupGet(b => b.Name).Returns("blank");

        var sut = new DefaultAttachResolver(new[] { validBackend.Object, blankBackend.Object }, new NullRemoteMountProvider());
        var mounts = sut.GetRootMounts(null);

        if (mounts.Count != 1 || mounts[0].MountPath != "/" + safe)
        {
            return false;
        }

        var firstClone = mounts[0].Target.CreateSession();
        firstClone.Dialect = NinePDialect.NineP2000U;
        var secondClone = mounts[0].Target.CreateSession();

        return firstClone is MarkerFileSystem first &&
               secondClone is MarkerFileSystem second &&
               mounts[0].Target.Id == "valid" &&
               first.Id != second.Id;
    }

    [Fact]
    public void ResolveAsync_Missing_Local_And_Remote_Throws_ProtocolException()
    {
        var sut = new DefaultAttachResolver(Array.Empty<IProtocolBackend>(), new NullRemoteMountProvider());

        Action act = () => sut.ResolveAsync("/missing", null, null).Sync();

        act.Should().Throw<NinePProtocolException>()
            .WithMessage("*missing*");
    }

    [Fact]
    public void RemoteProvider_Null_Tasks_And_Snapshots_Are_Treated_As_Empty()
    {
        var sut = new DefaultAttachResolver(Array.Empty<IProtocolBackend>(), new BrokenRemoteMountProvider());

        var mounts = sut.GetRemoteMountPathsAsync().Sync();
        var resolved = sut.TryCreateRemoteFileSystemAsync("/missing").Sync();

        mounts.Should().BeEmpty();
        resolved.Should().BeNull();
    }

    private static string NormalizeName(string raw)
    {
        var chars = raw.Where(char.IsLetterOrDigit).Take(16).ToArray();
        return chars.Length == 0 ? "mount" : new string(chars);
    }

    private sealed class RecordingClusterManager : IRemoteMountProvider
    {
        public List<string> RequestedMounts { get; } = new();
        public Func<string, Task<INinePFileSystem?>> RemoteFactory { get; set; } =
            _ => Task.FromResult<INinePFileSystem?>(null);

        public void Start()
        {
        }

        public Task StopAsync() => Task.CompletedTask;

        public Task RegisterMountAsync(string mountPath, Func<INinePFileSystem> createSession) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetRemoteMountPathsAsync() => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public async Task<INinePFileSystem?> TryCreateRemoteFileSystemAsync(string mountPath)
        {
            RequestedMounts.Add(mountPath);
            return await RemoteFactory(mountPath);
        }

        public void Dispose()
        {
        }
    }

    private sealed class BrokenRemoteMountProvider : IRemoteMountProvider
    {
        public void Start()
        {
        }

        public Task StopAsync() => Task.CompletedTask;

        public Task RegisterMountAsync(string mountPath, Func<INinePFileSystem> createSession) => Task.CompletedTask;

#pragma warning disable CS8603
        public Task<IReadOnlyList<string>> GetRemoteMountPathsAsync() => null;

        public Task<INinePFileSystem?> TryCreateRemoteFileSystemAsync(string mountPath) => null;
#pragma warning restore CS8603

        public void Dispose()
        {
        }
    }

    private sealed class MarkerFileSystem : INinePFileSystem
    {
        public MarkerFileSystem(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public NinePDialect Dialect { get; set; }

        public Task<Rwalk> WalkAsync(Twalk twalk) => Task.FromResult(new Rwalk(twalk.Tag, Array.Empty<Qid>()));
        public Task<Ropen> OpenAsync(Topen topen) => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 1), 0));
        public Task<Rread> ReadAsync(Tread tread) => Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        public Task<Rwrite> WriteAsync(Twrite twrite) => Task.FromResult(new Rwrite(twrite.Tag, (uint)twrite.Count));
        public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
        public Task<Rstat> StatAsync(Tstat tstat) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0, 0, 0, 0, Id, "u", "g", "m")));
        public Task<Rwstat> WstatAsync(Twstat twstat) => Task.FromResult(new Rwstat(twstat.Tag));
        public Task<Rremove> RemoveAsync(Tremove tremove) => Task.FromResult(new Rremove(tremove.Tag));
        public INinePFileSystem Clone() => new MarkerFileSystem(Id + ":clone") { Dialect = Dialect };
    }
}
