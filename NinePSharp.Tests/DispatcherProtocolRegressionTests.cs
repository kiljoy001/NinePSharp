using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests;

public sealed class DispatcherProtocolRegressionTests
{
    [Fact]
    public async Task Dispatcher_Treaddir_On_Backend_Uses_Rreaddir_Response()
    {
        var runtime = new ReaddirRuntime();
        var backend = new Mock<IProtocolBackend>(MockBehavior.Strict);
        backend.SetupGet(x => x.Name).Returns("rd");
        backend.SetupGet(x => x.MountPath).Returns("/rd");
        backend.Setup(x => x.GetRuntime(It.IsAny<SecureString?>(), It.IsAny<X509Certificate2?>())).Returns(runtime);
        backend.Setup(x => x.GetRuntime(It.IsAny<X509Certificate2?>())).Returns(runtime);

        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new[] { backend.Object },
            new NullRemoteMountProvider());

        await dispatcher.DispatchAsync("s", NinePMessage.NewMsgTattach(new Tattach(1, 1, NinePConstants.NoFid, "user", "rd")), NinePDialect.NineP2000);
        await dispatcher.DispatchAsync("s", NinePMessage.NewMsgTopen(new Topen(2, 1, NinePConstants.OREAD)), NinePDialect.NineP2000);

        var response = await dispatcher.DispatchAsync("s", NinePMessage.NewMsgTreaddir(new Treaddir(24, 3, 1, 0, 4096)), NinePDialect.NineP2000);

        response.Should().BeOfType<Rreaddir>();
        runtime.ReaddirCalls.Should().Be(1);
        runtime.ReadCalls.Should().Be(0);
    }

    [Property(MaxTest = 40)]
    public bool Dispatcher_VirtualTreaddir_Paging_Is_Deterministic_For_Client_Offsets(string[] rawMounts)
    {
        var names = rawMounts
            .Select((value, index) => DispatcherIntegrationTestKit.CleanMount(value, index))
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();

        if (names.Length == 0)
        {
            names = new[] { "alpha", "beta", "gamma" };
        }

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(
            names.Select(name => (IProtocolBackend)new StubBackend("/" + name, () => new MarkerFileSystem(name))).ToArray());

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100).Sync();
        DispatcherIntegrationTestKit.OpenAsync(dispatcher, 2, 100).Sync();

        var first = DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, 3, 100, 0, 96).Sync();
        var firstEntries = ParseReaddirEntries(first.Data.Span);
        if (firstEntries.Count == 0)
        {
            return true;
        }

        ulong nextOffset = firstEntries[^1].NextOffset;
        var second = DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, 4, 100, nextOffset, 96).Sync();
        var reset = DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, 5, 100, 0, 96).Sync();
        var secondAgain = DispatcherIntegrationTestKit.ReaddirAsync(dispatcher, 6, 100, nextOffset, 96).Sync();

        return second.Data.ToArray().SequenceEqual(secondAgain.Data.ToArray())
            && first.Data.ToArray().SequenceEqual(reset.Data.ToArray());
    }

    [Fact]
    public async Task Dispatcher_Read_Requires_Open_But_Open_Then_Read_Succeeds()
    {
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[]
        {
            new StubBackend("/mock", () => new MarkerFileSystem("payload"))
        });

        await DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100);
        await DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "mock" });

        var beforeOpen = await dispatcher.DispatchAsync("test-session", NinePMessage.NewMsgTread(new Tread(3, 101, 0, 32)), NinePDialect.NineP2000);
        beforeOpen.Should().BeOfType<Rerror>();

        await DispatcherIntegrationTestKit.OpenAsync(dispatcher, 4, 101);
        var afterOpen = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, 5, 101, 0, 32);

        DispatcherIntegrationTestKit.ReadPayload(afterOpen).Should().Be("payload");
    }

    [Fact]
    public async Task DefaultAttachResolver_Remote_Fallback_Uses_Mount_List_Without_Runtime_Probe()
    {
        var remoteMounts = new Mock<IRemoteMountProvider>(MockBehavior.Strict);
        remoteMounts.Setup(x => x.GetRemoteMountPathsAsync()).ReturnsAsync(new[] { "/remote" });

        var resolver = new DefaultAttachResolver(Array.Empty<IProtocolBackend>(), remoteMounts.Object);
        var result = await resolver.ResolveAsync("remote", null, null);

        result.Target.Should().NotBeNull();
        result.Target!.IsRemote.Should().BeTrue();
        result.Target.MountPath.Should().Be("/remote");
        remoteMounts.Verify(x => x.GetRemoteMountPathsAsync(), Times.Once);
        remoteMounts.Verify(x => x.TryCreateRemoteRuntimeAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Namespace_UnmountByMountId_Removes_Only_Selected_Mount()
    {
        var targetA = BackendTargetDescriptor.LocalRuntime("a", "/m", () => RuntimeFileSystemAdapter.ToRuntime(new MarkerFileSystem("a")));
        var targetB = BackendTargetDescriptor.LocalRuntime("b", "/m", () => RuntimeFileSystemAdapter.ToRuntime(new MarkerFileSystem("b")));
        var mountPath = Microsoft.FSharp.Collections.ListModule.OfSeq(new[] { "mnt" });
        var mount = new NinePSharp.Core.FSharp.MountChain(
            1UL,
            NinePSharp.Core.FSharp.NamespaceOps.mountKeyForPath(mountPath),
            mountPath,
            Microsoft.FSharp.Collections.ListModule.OfSeq(new[]
            {
                new NinePSharp.Core.FSharp.MountBranch(targetA, NinePSharp.Core.FSharp.BindFlags.MCREATE),
                new NinePSharp.Core.FSharp.MountBranch(targetB, NinePSharp.Core.FSharp.BindFlags.MAFTER)
            }));
        var ns = NinePSharp.Core.FSharp.NamespaceOps.mount(mount.From, mount, NinePSharp.Core.FSharp.NamespaceOps.empty);

        var updated = NinePSharp.Core.FSharp.NamespaceOps.unmountByMountId(1UL, ns);
        var remaining = updated.MountHash.Values.ToList();

        remaining.Should().HaveCount(1);
        remaining[0].MountId.Should().Be(1UL);
        remaining[0].Branches.ToList().Should().HaveCount(1);
        remaining[0].Branches.Head.Target.Id.Should().Be("a");
    }

    private static List<DispatcherIntegrationTestKit.ReaddirEntry> ParseReaddirEntries(ReadOnlySpan<byte> data)
    {
        var entries = new List<DispatcherIntegrationTestKit.ReaddirEntry>();
        int offset = 0;
        while (offset + 24 <= data.Length)
        {
            var qidType = (QidType)data[offset];
            ulong nextOffset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 13, 8));
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 22, 2));
            int entrySize = 24 + nameLength;
            if (entrySize <= 0 || offset + entrySize > data.Length)
            {
                break;
            }

            var name = Encoding.UTF8.GetString(data.Slice(offset + 24, nameLength));
            entries.Add(new DispatcherIntegrationTestKit.ReaddirEntry(qidType, nextOffset, name));
            offset += entrySize;
        }

        return entries;
    }

    private sealed class ReaddirRuntime : IBackendRuntime, IReaddirCapableBackendRuntime
    {
        public string Id => "rd";
        public string MountPath => "/rd";
        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;
        public int ReaddirCalls { get; private set; }
        public int ReadCalls { get; private set; }

        public Task<Rwalk> WalkAsync(string[] relativePath, NinePDialect dialect)
            => Task.FromResult(new Rwalk(0, Array.Empty<Qid>()));

        public Task<Ropen> OpenAsync(string[] relativePath, Topen topen, NinePDialect dialect)
            => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 1), 8192));

        public Task<Rread> ReadAsync(string[] relativePath, Tread tread, NinePDialect dialect, CancellationToken ct = default)
        {
            ReadCalls++;
            return Task.FromResult(new Rread(tread.Tag, Array.Empty<byte>()));
        }

        public Task<Rreaddir> ReaddirAsync(string[] relativePath, Treaddir treaddir, NinePDialect dialect)
        {
            ReaddirCalls++;
            return Task.FromResult(new Rreaddir((uint)(NinePConstants.HeaderSize + 4), treaddir.Tag, 0, Array.Empty<byte>()));
        }

        public Task<Rwrite> WriteAsync(string[] relativePath, Twrite twrite, NinePDialect dialect, CancellationToken ct = default)
            => Task.FromResult(new Rwrite(twrite.Tag, twrite.Count));

        public Task<Rclunk> ClunkAsync(string[] relativePath, Tclunk tclunk, NinePDialect dialect)
            => Task.FromResult(new Rclunk(tclunk.Tag));

        public Task<Rstat> StatAsync(string[] relativePath, Tstat tstat, NinePDialect dialect)
            => Task.FromResult(new Rstat(tstat.Tag, new Stat()));

        public Task<Rwstat> WstatAsync(string[] relativePath, Twstat twstat, NinePDialect dialect)
            => Task.FromResult(new Rwstat(twstat.Tag));

        public Task<Rremove> RemoveAsync(string[] relativePath, Tremove tremove, NinePDialect dialect)
            => Task.FromResult(new Rremove(tremove.Tag));

        public Task<Rcreate> CreateAsync(string[] parentRelativePath, Tcreate tcreate, NinePDialect dialect)
            => Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, 2), 8192));
    }

    private sealed class NullRemoteMountProvider : IRemoteMountProvider
    {
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public Task RegisterMountAsync(string mountPath, Func<IBackendRuntime> createRuntime) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetRemoteMountPathsAsync() => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IBackendRuntime?> TryCreateRemoteRuntimeAsync(string mountPath) => Task.FromResult<IBackendRuntime?>(null);
        public void Dispose() { }
    }
}
