using NinePSharp.Constants;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Configuration;
using Moq;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Cluster.Actors;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests;

public class RootFileSystemMutationPropertyTests
{
    private readonly record struct ReaddirEntry(QidType QidType, ulong NextOffset, byte TypeByte, string Name);

    [Property(MaxTest = 35)]
    public void ReadAsync_Serializes_Sorted_Directory_Stats(string[] rawMounts)
    {
        if (rawMounts == null) return;

        var mounts = rawMounts
            .Select((n, i) => CleanMount(n, i))
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToList();

        if (mounts.Count < 3) return;

        var backends = mounts
            .Select(m => (IProtocolBackend)new StubBackend("/" + m, () => new MockFileSystem(new LuxVaultService())))
            .ToList();

        var rootFs = new RootFileSystem(backends, null);
        var read = rootFs.ReadAsync(new Tread(1, 0, 0, 65535)).Sync();
        var stats = ParseStatsTable(read.Data.Span);

        stats.Should().NotBeEmpty();
        stats.Select(s => s.Name).Should().Equal(mounts.OrderBy(m => m, StringComparer.Ordinal));

        foreach (var stat in stats)
        {
            stat.Qid.Type.Should().Be(QidType.QTDIR);
            (stat.Mode & (uint)NinePConstants.FileMode9P.DMDIR).Should().NotBe(0);
        }
    }

    [Property(MaxTest = 30)]
    public void ReadAsync_Respects_Entry_Boundaries_And_Offsets(string[] rawMounts)
    {
        if (rawMounts == null) return;

        var mounts = rawMounts
            .Select((n, i) => CleanMount(n, i))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();

        if (mounts.Count < 4) return;

        var backends = mounts
            .Select(m => (IProtocolBackend)new StubBackend("/" + m, () => new MockFileSystem(new LuxVaultService())))
            .ToList();

        var rootFs = new RootFileSystem(backends, null);
        var full = rootFs.ReadAsync(new Tread(1, 0, 0, 65535)).Sync();

        if (full.Data.Length < 2) return;

        int firstEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(full.Data.Span.Slice(0, 2)) + 2;
        if (firstEntrySize <= 1 || firstEntrySize > full.Data.Length) return;

        var exactOne = rootFs.ReadAsync(new Tread(2, 0, 0, (uint)firstEntrySize)).Sync();
        exactOne.Data.Length.Should().Be(firstEntrySize);

        var undersized = rootFs.ReadAsync(new Tread(3, 0, 0, (uint)(firstEntrySize - 1))).Sync();
        undersized.Data.Length.Should().Be(0);

        if (full.Data.Length > firstEntrySize)
        {
            var secondPage = rootFs.ReadAsync(new Tread(4, 0, (ulong)firstEntrySize, 65535)).Sync();
            secondPage.Data.Length.Should().BeGreaterThan(0);
        }

        var atEof = rootFs.ReadAsync(new Tread(5, 0, (ulong)full.Data.Length, 4096)).Sync();
        atEof.Data.Length.Should().Be(0);
    }

    [Fact]
    public async Task Delegated_Open_And_Clunk_Are_Forwarded()
    {
        var fsMock = new Mock<INinePFileSystem>(MockBehavior.Strict);
        fsMock.SetupProperty(f => f.Dialect);
        fsMock.Setup(f => f.Clone()).Returns(fsMock.Object);
        fsMock.Setup(f => f.WalkAsync(It.IsAny<Twalk>()))
            .ReturnsAsync((Twalk t) => new Rwalk(t.Tag, new[] { new Qid(QidType.QTDIR, 0, 10) }));
        fsMock.Setup(f => f.OpenAsync(It.IsAny<Topen>()))
            .ReturnsAsync((Topen t) => new Ropen(t.Tag, new Qid(QidType.QTFILE, 0, 0xABCDEF), 777));
        fsMock.Setup(f => f.ClunkAsync(It.IsAny<Tclunk>()))
            .ReturnsAsync((Tclunk t) => new Rclunk(t.Tag));

        var backend = new StubBackend("/delegated", () => fsMock.Object);
        var rootFs = new RootFileSystem(new List<IProtocolBackend> { backend }, null);

        await rootFs.WalkAsync(new Twalk(1, 0, 1, new[] { "delegated" }));
        var open = await rootFs.OpenAsync(new Topen(2, 1, 0));
        var clunk = await rootFs.ClunkAsync(new Tclunk(3, 1));

        open.Qid.Path.Should().Be(0xABCDEF);
        open.Iounit.Should().Be(777);
        clunk.Tag.Should().Be(3);
        fsMock.Verify(f => f.OpenAsync(It.IsAny<Topen>()), Times.Once);
        fsMock.Verify(f => f.ClunkAsync(It.IsAny<Tclunk>()), Times.Once);
    }

    [Fact]
    public async Task Walk_Prefers_Local_Backend_Over_Cluster_When_Both_Available()
    {
        var system = ActorSystem.Create("root-walk-local-preference");
        try
        {
            var remoteBackendActor = system.ActorOf(Props.Create(() => new RemoteBackendActor(9999)));
            var registry = system.ActorOf(Props.Create(() => new TestRegistryActor(remoteBackendActor, new[] { "/remote-a" }, "/dupe")));
            var clusterManager = new TestClusterManager(system, registry);

            var localFs = new Mock<INinePFileSystem>(MockBehavior.Strict);
            localFs.SetupProperty(f => f.Dialect);
            localFs.Setup(f => f.Clone()).Returns(localFs.Object);
            localFs.Setup(f => f.WalkAsync(It.IsAny<Twalk>()))
                .ReturnsAsync((Twalk t) => new Rwalk(t.Tag, t.Wname.Select(_ => new Qid(QidType.QTFILE, 0, 111)).ToArray()));

            var localBackend = new StubBackend("/dupe", () => localFs.Object);
            var rootFs = new RootFileSystem(new List<IProtocolBackend> { localBackend }, clusterManager);

            var walk = await rootFs.WalkAsync(new Twalk(1, 0, 1, new[] { "dupe", "child" }));

            walk.Wqid.Should().HaveCount(2);
            walk.Wqid[0].Type.Should().Be(QidType.QTDIR);
            walk.Wqid[1].Path.Should().Be(111);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact]
    public async Task Readdir_Includes_Cluster_Snapshot_Mounts()
    {
        var system = ActorSystem.Create("root-readdir-cluster-snapshot");
        try
        {
            var remoteBackendActor = system.ActorOf(Props.Create(() => new RemoteBackendActor(8001)));
            var registry = system.ActorOf(Props.Create(() => new TestRegistryActor(remoteBackendActor, new[] { "/remote-a", "/remote-b" }, "/none")));
            var clusterManager = new TestClusterManager(system, registry);

            var rootFs = new RootFileSystem(
                new List<IProtocolBackend> { new StubBackend("/local", () => new MockFileSystem(new LuxVaultService())) },
                clusterManager);

            var readdir = await rootFs.ReaddirAsync(new Treaddir(1, 0, 1, 0, 8192));
            var entries = ParseReaddirEntries(readdir.Data.Span);
            var names = entries.Select(e => e.Name).ToList();

            names.Should().Contain(new[] { "local", "remote-a", "remote-b" });
            names.Should().Equal(names.OrderBy(n => n, StringComparer.Ordinal));
            readdir.Size.Should().Be((uint)(readdir.Count + NinePConstants.HeaderSize + 4));

            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].QidType.Should().Be(QidType.QTDIR);
                entries[i].TypeByte.Should().Be((byte)QidType.QTDIR);
                entries[i].NextOffset.Should().Be((ulong)(i + 1));
            }
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact]
    public async Task Walk_Can_Return_To_Root_And_Enter_Another_Backend_Without_Clunk()
    {
        int alphaSessions = 0;
        int betaSessions = 0;

        var alphaFs = new Mock<INinePFileSystem>(MockBehavior.Strict);
        alphaFs.SetupProperty(f => f.Dialect);
        alphaFs.Setup(f => f.Clone()).Returns(alphaFs.Object);
        alphaFs.Setup(f => f.WalkAsync(It.IsAny<Twalk>()))
            .ReturnsAsync((Twalk t) => new Rwalk(t.Tag, new[] { new Qid(QidType.QTFILE, 0, 0xA1) }));

        var betaFs = new Mock<INinePFileSystem>(MockBehavior.Strict);
        betaFs.SetupProperty(f => f.Dialect);
        betaFs.Setup(f => f.Clone()).Returns(betaFs.Object);
        betaFs.Setup(f => f.WalkAsync(It.IsAny<Twalk>()))
            .ReturnsAsync((Twalk t) => new Rwalk(t.Tag, new[] { new Qid(QidType.QTFILE, 0, 0xB2) }));

        var rootFs = new RootFileSystem(
            new List<IProtocolBackend>
            {
                new StubBackend("/alpha", () => { alphaSessions++; return alphaFs.Object; }),
                new StubBackend("/beta", () => { betaSessions++; return betaFs.Object; })
            },
            null);

        var enterAlpha = await rootFs.WalkAsync(new Twalk(1, 0, 1, new[] { "alpha" }));
        var backToRoot = await rootFs.WalkAsync(new Twalk(2, 1, 1, new[] { ".." }));
        var enterBeta = await rootFs.WalkAsync(new Twalk(3, 1, 1, new[] { "beta" }));

        enterAlpha.Wqid.Should().ContainSingle();
        backToRoot.Wqid.Should().NotBeNull();
        alphaSessions.Should().Be(1);
        betaSessions.Should().Be(1, "walking to \"..\" should clear root delegation so a second backend can be entered");

        enterBeta.Wqid.Should().ContainSingle();
        enterBeta.Wqid[0].Type.Should().Be(QidType.QTDIR);
    }

    [Fact]
    public async Task Readdir_NonZero_Offset_Returns_Next_Page_Until_Eof()
    {
        var backendNames = Enumerable.Range(0, 40).Select(i => $"backend{i:00}").ToList();
        var backends = backendNames
            .Select(name => (IProtocolBackend)new StubBackend("/" + name, () => new MockFileSystem(new LuxVaultService())))
            .ToList();

        var rootFs = new RootFileSystem(backends, null);
        const uint pageBytes = 330; // 10 entries when names are "backendNN" (33 bytes each)

        var firstPage = await rootFs.ReaddirAsync(new Treaddir(1, 0, 1, 0, pageBytes));
        var firstEntries = ParseReaddirEntries(firstPage.Data.Span);
        firstEntries.Should().NotBeEmpty();
        firstEntries.Count.Should().BeLessThan(backendNames.Count);

        ulong nextOffset = firstEntries[^1].NextOffset;
        var secondPage = await rootFs.ReaddirAsync(new Treaddir(2, 0, 1, nextOffset, pageBytes));
        secondPage.Count.Should().BeGreaterThan(0, "non-zero offsets should return subsequent root directory entries");

        var secondEntries = ParseReaddirEntries(secondPage.Data.Span);
        secondEntries.Should().NotBeEmpty();

        var allSeen = firstEntries.Select(e => e.Name).Concat(secondEntries.Select(e => e.Name)).ToList();
        allSeen.Should().OnlyHaveUniqueItems();
        allSeen.Should().OnlyContain(name => backendNames.Contains(name));
    }

    [Property(MaxTest = 30)]
    public void Walk_BackToRoot_Then_SwitchBackend_WithoutClunk_Property(string rawAlpha, string rawBeta)
    {
        var alphaName = CleanMount(rawAlpha, 1);
        var betaName = CleanMount(rawBeta, 2);
        if (alphaName == betaName) betaName += "_b";

        int alphaSessions = 0;
        int betaSessions = 0;

        var alphaFs = new Mock<INinePFileSystem>(MockBehavior.Strict);
        alphaFs.SetupProperty(f => f.Dialect);
        alphaFs.Setup(f => f.Clone()).Returns(alphaFs.Object);
        alphaFs.Setup(f => f.WalkAsync(It.IsAny<Twalk>()))
            .ReturnsAsync((Twalk t) => new Rwalk(t.Tag, new[] { new Qid(QidType.QTFILE, 0, 0xAA) }));

        var betaFs = new Mock<INinePFileSystem>(MockBehavior.Strict);
        betaFs.SetupProperty(f => f.Dialect);
        betaFs.Setup(f => f.Clone()).Returns(betaFs.Object);
        betaFs.Setup(f => f.WalkAsync(It.IsAny<Twalk>()))
            .ReturnsAsync((Twalk t) => new Rwalk(t.Tag, new[] { new Qid(QidType.QTFILE, 0, 0xBB) }));

        var rootFs = new RootFileSystem(
            new List<IProtocolBackend>
            {
                new StubBackend("/" + alphaName, () => { alphaSessions++; return alphaFs.Object; }),
                new StubBackend("/" + betaName, () => { betaSessions++; return betaFs.Object; })
            },
            null);

        rootFs.WalkAsync(new Twalk(1, 0, 1, new[] { alphaName })).Sync();
        rootFs.WalkAsync(new Twalk(2, 1, 1, new[] { ".." })).Sync();
        var switchWalk = rootFs.WalkAsync(new Twalk(3, 1, 1, new[] { betaName })).Sync();

        alphaSessions.Should().Be(1);
        betaSessions.Should().Be(1, "delegation should be released by walking to root so another backend can be entered");
        switchWalk.Wqid.Should().ContainSingle();
        switchWalk.Wqid[0].Type.Should().Be(QidType.QTDIR);
    }

    [Property(MaxTest = 35)]
    public void Readdir_Pagination_NonZeroOffset_Returns_FollowOnEntries_Property(PositiveInt backendCountSeed, PositiveInt entriesPerPageSeed)
    {
        int backendCount = Math.Clamp(backendCountSeed.Get % 48 + 12, 12, 60);
        int entriesPerPage = Math.Clamp(entriesPerPageSeed.Get % 8 + 1, 1, 8);
        var mounts = Enumerable.Range(0, backendCount).Select(i => $"b{i:000}").ToList();

        var backends = mounts
            .Select(name => (IProtocolBackend)new StubBackend("/" + name, () => new MockFileSystem(new LuxVaultService())))
            .ToList();

        var rootFs = new RootFileSystem(backends, null);
        const uint entrySize = 28; // qid(13) + offset(8) + type(1) + nameLen(2) + name(4)
        uint pageBytes = (uint)entriesPerPage * entrySize;

        var firstPage = rootFs.ReaddirAsync(new Treaddir(1, 0, 1, 0, pageBytes)).Sync();
        var firstEntries = ParseReaddirEntries(firstPage.Data.Span);
        firstEntries.Should().NotBeEmpty();
        firstEntries.Count.Should().Be(entriesPerPage);

        ulong nextOffset = firstEntries[^1].NextOffset;
        var secondPage = rootFs.ReaddirAsync(new Treaddir(2, 0, 1, nextOffset, pageBytes)).Sync();
        secondPage.Count.Should().BeGreaterThan(0, "a non-zero readdir offset should return the next page while entries remain");

        var secondEntries = ParseReaddirEntries(secondPage.Data.Span);
        secondEntries.Should().NotBeEmpty();
        firstEntries.Select(e => e.Name).Intersect(secondEntries.Select(e => e.Name)).Should().BeEmpty();
    }

    [Fact]
    public async Task Walk_Fuzz_BackendSwitch_DoesNotRequire_Clunk()
    {
        var random = new Random(20260226);

        for (int i = 0; i < 60; i++)
        {
            string alphaName = $"alpha{i:00}_{random.Next(1000, 9999)}";
            string betaName = $"beta{i:00}_{random.Next(1000, 9999)}";

            int alphaSessions = 0;
            int betaSessions = 0;

            var alphaFs = new Mock<INinePFileSystem>(MockBehavior.Strict);
            alphaFs.SetupProperty(f => f.Dialect);
            alphaFs.Setup(f => f.Clone()).Returns(alphaFs.Object);
            alphaFs.Setup(f => f.WalkAsync(It.IsAny<Twalk>()))
                .ReturnsAsync((Twalk t) => new Rwalk(t.Tag, new[] { new Qid(QidType.QTFILE, 0, (ulong)(1000 + i)) }));

            var betaFs = new Mock<INinePFileSystem>(MockBehavior.Strict);
            betaFs.SetupProperty(f => f.Dialect);
            betaFs.Setup(f => f.Clone()).Returns(betaFs.Object);
            betaFs.Setup(f => f.WalkAsync(It.IsAny<Twalk>()))
                .ReturnsAsync((Twalk t) => new Rwalk(t.Tag, new[] { new Qid(QidType.QTFILE, 0, (ulong)(2000 + i)) }));

            var rootFs = new RootFileSystem(
                new List<IProtocolBackend>
                {
                    new StubBackend("/" + alphaName, () => { alphaSessions++; return alphaFs.Object; }),
                    new StubBackend("/" + betaName, () => { betaSessions++; return betaFs.Object; })
                },
                null);

            await rootFs.WalkAsync(new Twalk(1, 0, 1, new[] { alphaName }));
            await rootFs.WalkAsync(new Twalk(2, 1, 1, new[] { ".." }));
            await rootFs.WalkAsync(new Twalk(3, 1, 1, new[] { betaName }));

            betaSessions.Should().Be(1, "fuzz iteration {0} should be able to switch backends without clunk", i);
        }
    }

    [Fact]
    public async Task Readdir_Fuzz_Pagination_Enumerates_All_Backends()
    {
        var random = new Random(7331);
        const uint entrySize = 28; // qid(13) + offset(8) + type(1) + nameLen(2) + name(4)

        for (int iteration = 0; iteration < 30; iteration++)
        {
            int backendCount = random.Next(18, 55);
            var backendNames = Enumerable.Range(0, backendCount)
                .Select(i => $"f{i:000}")
                .ToList();

            var backends = backendNames
                .Select(name => (IProtocolBackend)new StubBackend("/" + name, () => new MockFileSystem(new LuxVaultService())))
                .ToList();

            var rootFs = new RootFileSystem(backends, null);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ulong offset = 0;

            for (int step = 0; step < backendCount + 5; step++)
            {
                uint entriesPerPage = (uint)random.Next(1, 7);
                uint pageBytes = entriesPerPage * entrySize;
                var page = await rootFs.ReaddirAsync(new Treaddir((ushort)(step + 1), 0, 1, offset, pageBytes));
                var entries = ParseReaddirEntries(page.Data.Span);

                if (entries.Count == 0) break;

                foreach (var entry in entries)
                {
                    seen.Add(entry.Name);
                }

                offset = entries[^1].NextOffset;
            }

            seen.Should().BeEquivalentTo(
                backendNames.OrderBy(n => n, StringComparer.Ordinal),
                "fuzz iteration {0} should be able to page through the full root mount list", iteration);
        }
    }

    private static string CleanMount(string? raw, int index)
    {
        if (string.IsNullOrWhiteSpace(raw)) return $"m{index}";
        var chars = raw
            .Where(c => char.IsLetterOrDigit(c) || c is '_' or '-')
            .Take(20)
            .ToArray();

        return chars.Length == 0 ? $"m{index}" : new string(chars);
    }

    private static List<Stat> ParseStatsTable(ReadOnlySpan<byte> data)
    {
        var result = new List<Stat>();
        int offset = 0;
        while (offset < data.Length)
        {
            result.Add(new Stat(data, ref offset));
        }
        return result;
    }

    private static List<ReaddirEntry> ParseReaddirEntries(ReadOnlySpan<byte> data)
    {
        var result = new List<ReaddirEntry>();
        int offset = 0;

        while (offset < data.Length)
        {
            if (data.Length - offset < 24)
            {
                throw new Xunit.Sdk.XunitException($"Malformed root readdir entry at byte {offset}");
            }

            var qidType = (QidType)data[offset];
            offset += 1 + 4 + 8; // qid fields

            ulong nextOffset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            offset += 8;

            byte typeByte = data[offset++];
            ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;

            if (nameLen > data.Length - offset)
            {
                throw new Xunit.Sdk.XunitException($"Invalid root readdir name length {nameLen}");
            }

            string name = Encoding.UTF8.GetString(data.Slice(offset, nameLen));
            offset += nameLen;

            result.Add(new ReaddirEntry(qidType, nextOffset, typeByte, name));
        }

        return result;
    }

    private sealed class StubBackend : IProtocolBackend
    {
        private readonly Func<INinePFileSystem> _factory;

        public StubBackend(string mountPath, Func<INinePFileSystem> factory)
        {
            MountPath = mountPath;
            _factory = factory;
        }

        public string Name => MountPath.Trim('/');
        public string MountPath { get; }

        public Task InitializeAsync(IConfiguration configuration) => Task.CompletedTask;
        public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => _factory();
        public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => _factory();
    }

    private sealed class TestClusterManager : IClusterManager
    {
        public TestClusterManager(ActorSystem system, IActorRef registry)
        {
            System = system;
            Registry = registry;
        }

        public void Start() { }
        public Task StopAsync() => System?.Terminate() ?? Task.CompletedTask;
        public ActorSystem? System { get; }
        public IActorRef? Registry { get; }
        public void Dispose() { }
    }

    private sealed class TestRegistryActor : ReceiveActor
    {
        public TestRegistryActor(IActorRef remoteBackendActor, IReadOnlyList<string> snapshotMounts, string clusterMountPath)
        {
            Receive<GetBackends>(_ => Sender.Tell(new BackendsSnapshot(snapshotMounts.ToArray())));
            Receive<GetBackend>(msg =>
            {
                if (msg.MountPath == clusterMountPath)
                {
                    Sender.Tell(new BackendFound(clusterMountPath, remoteBackendActor));
                }
                else
                {
                    Sender.Tell(new BackendNotFound(msg.MountPath));
                }
            });
        }
    }

    private sealed class RemoteBackendActor : ReceiveActor
    {
        private readonly IActorRef _session;

        public RemoteBackendActor(ulong path)
        {
            _session = Context.ActorOf(Props.Create(() => new RemoteSessionActor(path)));
            Receive<SpawnSession>(_ => Sender.Tell(new SessionSpawned(_session)));
        }
    }

    private sealed class RemoteSessionActor : ReceiveActor
    {
        public RemoteSessionActor(ulong path)
        {
            Receive<TWalkDto>(msg =>
            {
                var qids = msg.Wname.Select(_ => new Qid(QidType.QTFILE, 0, path)).ToArray();
                Sender.Tell(new RWalkDto { Tag = msg.Tag, Wqid = qids });
            });
        }
    }
}
