using System;
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
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests;

public sealed class DispatcherNamespaceDeterminismPropertyFuzzTests
{
    [Property(MaxTest = 60)]
    public bool Dispatcher_RootWalk_Uses_Deterministic_DirectoryQids(NonEmptyString rawMount)
    {
        string mount = DispatcherIntegrationTestKit.CleanMount(rawMount.Get, 1);
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new[]
        {
            (IProtocolBackend)new StubBackend("/" + mount, () => new MarkerFileSystem("marker:" + mount))
        });

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, tag: 1, fid: 100).Sync();
        var walk = DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: 100, newFid: 101, wname: new[] { mount }).Sync();

        return walk.Wqid is { Length: 1 }
            && walk.Wqid[0].Type == QidType.QTDIR
            && walk.Wqid[0].Path == StableSyntheticQidPath('d', "/" + mount);
    }

    [Property(MaxTest = 60)]
    public bool Dispatcher_DirectAttach_Nested_Walk_Uses_Deterministic_FileQids(NonEmptyString rawMount, string[] rawSegments)
    {
        string mount = DispatcherIntegrationTestKit.CleanMount(rawMount.Get, 1);
        string[] segments = rawSegments
            .Select((s, i) => DispatcherIntegrationTestKit.CleanMount(s, i + 2))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(4)
            .ToArray();

        if (segments.Length == 0)
        {
            return true;
        }

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new[]
        {
            (IProtocolBackend)new StubBackend("/" + mount, () => new MarkerFileSystem("marker:" + mount))
        });

        DispatcherIntegrationTestKit.AttachAsync(dispatcher, tag: 1, fid: 200, aname: "/" + mount).Sync();
        var walk = DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: 2, fid: 200, newFid: 201, wname: segments).Sync();

        return walk.Wqid is { Length: > 0 }
            && walk.Wqid.Length == segments.Length
            && walk.Wqid[^1].Type == QidType.QTFILE
            && walk.Wqid[^1].Path == StableSyntheticQidPath('f', "/" + string.Join("/", segments));
    }

    [Property(MaxTest = 60)]
    public bool Dispatcher_DirectAttach_Dot_And_DotDot_Do_Not_Escape_Attached_Backend(byte[] operations)
    {
        string mount = "locked";
        string marker = "marker:" + mount;
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new[]
        {
            (IProtocolBackend)new StubBackend("/" + mount, () => new MarkerFileSystem(marker))
        });

        DispatcherIntegrationTestKit.AttachAsync(dispatcher, tag: 1, fid: 300, aname: "/" + mount).Sync();

        foreach (byte op in operations.Take(32))
        {
            string segment = (op & 1) == 0 ? "." : "..";
            var walk = DispatcherIntegrationTestKit.WalkAsync(dispatcher, tag: (ushort)(10 + op), fid: 300, newFid: 300, wname: new[] { segment }).Sync();
            if (walk.Wqid is null || walk.Wqid.Length != 1)
            {
                return false;
            }
        }

        var read = DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 400, fid: 300, offset: 0, count: 128).Sync();
        return DispatcherIntegrationTestKit.ReadPayload(read) == marker;
    }

    [Fact]
    public async Task Dispatcher_DirectAttach_By_Backend_Name_Reads_Backend_Payload()
    {
        var backend = new NamedStubBackend("vaultsvc", "/vaultx", () => new MarkerFileSystem("marker:vaultx"));
        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await DispatcherIntegrationTestKit.AttachAsync(dispatcher, tag: 1, fid: 401, aname: "vaultsvc");
        var read = await DispatcherIntegrationTestKit.ReadAsync(dispatcher, tag: 2, fid: 401, offset: 0, count: 128);

        DispatcherIntegrationTestKit.ReadPayload(read).Should().Be("marker:vaultx");
    }

    [Fact]
    public async Task Dispatcher_DirectAttach_Sessions_Get_Fresh_Backend_Instances()
    {
        int instance = 0;
        var backend = new NamedStubBackend(
            "sessioned",
            "/sessioned",
            () => new MarkerFileSystem("instance:" + Interlocked.Increment(ref instance)));

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new IProtocolBackend[] { backend });

        await AttachAsync(dispatcher, sessionId: "s1", tag: 1, fid: 500, aname: "/sessioned");
        await AttachAsync(dispatcher, sessionId: "s2", tag: 2, fid: 500, aname: "/sessioned");

        var read1 = await ReadAsync(dispatcher, sessionId: "s1", tag: 3, fid: 500);
        var read2 = await ReadAsync(dispatcher, sessionId: "s2", tag: 4, fid: 500);

        Encoding.UTF8.GetString(read1.Data.Span).Should().Be("instance:1");
        Encoding.UTF8.GetString(read2.Data.Span).Should().Be("instance:2");
    }

    private static ulong StableSyntheticQidPath(char kind, string path)
    {
        string normalized = NormalizeAbsolutePath(path);
        byte[] bytes = Encoding.UTF8.GetBytes($"{kind}:{normalized}");
        ulong hash = 14695981039346656037UL;

        foreach (byte value in bytes)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static string NormalizeAbsolutePath(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();

        foreach (string part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return stack.Count == 0 ? "/" : "/" + string.Join("/", stack);
    }

    private static async Task AttachAsync(NinePFSDispatcher dispatcher, string sessionId, ushort tag, uint fid, string aname)
    {
        var response = await dispatcher.DispatchAsync(
            sessionId,
            NinePMessage.NewMsgTattach(new Tattach(tag, fid, NinePConstants.NoFid, "user", aname)),
            NinePDialect.NineP2000U);

        Assert.IsType<Rattach>(response);
    }

    private static async Task<Rread> ReadAsync(NinePFSDispatcher dispatcher, string sessionId, ushort tag, uint fid)
    {
        var response = await dispatcher.DispatchAsync(
            sessionId,
            NinePMessage.NewMsgTread(new Tread(tag, fid, 0, 128)),
            NinePDialect.NineP2000U);

        return Assert.IsType<Rread>(response);
    }

    private sealed class NamedStubBackend : IProtocolBackend
    {
        private readonly Func<INinePFileSystem> _factory;

        public NamedStubBackend(string name, string mountPath, Func<INinePFileSystem> factory)
        {
            Name = name;
            MountPath = mountPath;
            _factory = factory;
        }

        public string Name { get; }
        public string MountPath { get; }

        public Task InitializeAsync(Microsoft.Extensions.Configuration.IConfiguration configuration) => Task.CompletedTask;
        public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null) => _factory();
        public INinePFileSystem GetFileSystem(SecureString? credentials, X509Certificate2? certificate = null) => _factory();
    }
}
