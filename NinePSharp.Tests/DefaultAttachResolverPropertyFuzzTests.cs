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

public class DefaultAttachResolverPropertyFuzzTests
{
    [Property(MaxTest = 50)]
    public bool ResolveAsync_Valid_Aname_Returns_Backend(NonEmptyString aname)
    {
        string safe = aname.Get.Replace("/", "").Trim();
        if (string.IsNullOrEmpty(safe)) return true;

        var localFs = new MarkerFileSystem("local");
        var backend = new Mock<IProtocolBackend>(MockBehavior.Strict);
        backend.SetupGet(b => b.MountPath).Returns("/" + safe);
        backend.SetupGet(b => b.Name).Returns("svc_" + safe);
        backend.Setup(b => b.GetRuntime(It.IsAny<SecureString?>(), It.IsAny<X509Certificate2?>()))
            .Returns(() => RuntimeFileSystemAdapter.ToRuntime(localFs));

        var cluster = new Mock<IRemoteMountProvider>();
        cluster.Setup(c => c.GetRemoteMountPathsAsync())
            .ReturnsAsync(Array.Empty<string>());

        var sut = new DefaultAttachResolver(new[] { backend.Object }, cluster.Object);
        var resolution = sut.ResolveAsync(safe, null, null).Result;

        return resolution.Target != null && resolution.Target.MountPath == "/" + safe;
    }

    [Property(MaxTest = 50)]
    public bool ResolveAsync_Remote_Fallback_Works(NonEmptyString aname)
    {
        string safe = aname.Get.Replace("/", "").Trim();
        if (string.IsNullOrEmpty(safe)) return true;

        var cluster = new Mock<IRemoteMountProvider>();
        cluster.Setup(c => c.GetRemoteMountPathsAsync())
            .ReturnsAsync(new[] { "/" + safe });

        var sut = new DefaultAttachResolver(Array.Empty<IProtocolBackend>(), cluster.Object);
        var resolution = sut.ResolveAsync(safe, null, null).Result;

        return resolution.Target != null && resolution.Target.IsRemote && resolution.Target.MountPath == "/" + safe;
    }

    [Fact]
    public void GetRootMounts_Filters_Invalid_And_Returns_Local_Runtimes()
    {
        var b1 = new Mock<IProtocolBackend>();
        b1.SetupGet(x => x.MountPath).Returns("/valid");
        b1.SetupGet(x => x.Name).Returns("b1");
        b1.Setup(x => x.GetRuntime(It.IsAny<X509Certificate2?>()))
          .Returns(() => RuntimeFileSystemAdapter.ToRuntime(new MarkerFileSystem("f1")));

        var b2 = new Mock<IProtocolBackend>();
        b2.SetupGet(x => x.MountPath).Returns(""); // invalid

        var sut = new DefaultAttachResolver(new[] { b1.Object, b2.Object }, new Mock<IRemoteMountProvider>().Object);
        var mounts = sut.GetRootMounts(null);

        mounts.Should().HaveCount(1);
        mounts[0].MountPath.Should().Be("/valid");
        mounts[0].Target.IsRemote.Should().BeFalse();
    }

    private sealed class MarkerFileSystem : INinePFileSystem
    {
        public string Id { get; }
        public MarkerFileSystem(string id) => Id = id;

        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

        public Task<Rwalk> WalkAsync(Twalk twalk) => Task.FromResult(new Rwalk(twalk.Tag, Array.Empty<Qid>()));
        public Task<Ropen> OpenAsync(Topen topen) => Task.FromResult(new Ropen(topen.Tag, new Qid(), 0));
        public Task<Rread> ReadAsync(Tread tread) => Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        public Task<Rwrite> WriteAsync(Twrite twrite) => Task.FromResult(new Rwrite(twrite.Tag, 0));
        public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
        public Task<Rstat> StatAsync(Tstat tstat) => Task.FromResult(new Rstat(tstat.Tag, new Stat()));
        public Task<Rwstat> WstatAsync(Twstat twstat) => Task.FromResult(new Rwstat(twstat.Tag));
        public Task<Rremove> RemoveAsync(Tremove tremove) => Task.FromResult(new Rremove(tremove.Tag));
        public Task<Rcreate> CreateAsync(Tcreate tcreate) => Task.FromResult(new Rcreate(tcreate.Tag, new Qid(), 0));
        public INinePFileSystem Clone() => new MarkerFileSystem(Id + ":clone") { Dialect = Dialect };
    }
}
