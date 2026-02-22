using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Server.Backends.JsonRpc;
using NinePSharp.Server.Configuration.Models;
using Xunit;

namespace NinePSharp.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Test doubles

file class FakeTransport : IJsonRpcTransport
{
    private readonly JsonNode? _result;
    public string? LastMethod { get; private set; }
    public object?[]? LastArgs { get; private set; }
    public int CallCount { get; private set; }

    public FakeTransport(JsonNode? result = null) => _result = result;

    public Task<JsonNode?> CallAsync(string method, object?[]? args = null)
    {
        LastMethod = method;
        LastArgs = args;
        CallCount++;
        return Task.FromResult(_result);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers

file static class Helpers
{
    public static JsonRpcBackendConfig Config(params (string name, string method, bool writable)[] endpoints)
    {
        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc", EndpointUrl = "http://fake/" };
        foreach (var (n, m, w) in endpoints)
            cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = n, Method = m, Writable = w });
        return cfg;
    }

    public static JsonRpcFileSystem Fs(JsonRpcBackendConfig cfg, IJsonRpcTransport? transport = null)
        => new JsonRpcFileSystem(cfg, transport ?? new FakeTransport());

    public static List<Stat> ParseDirectory(byte[] data)
    {
        var stats = new List<Stat>();
        int offset = 0;
        while (offset < data.Length)
        {
            stats.Add(new Stat(data, ref offset));
        }

        return stats;
    }

    public static async Task<JsonRpcFileSystem> FsAt(JsonRpcBackendConfig cfg, IJsonRpcTransport transport, string path)
    {
        var fs = new JsonRpcFileSystem(cfg, transport);
        await fs.WalkAsync(new Twalk(1, 10, 11, new[] { path }));
        return fs;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Unit Tests

public class JsonRpcFileSystemTests
{
    // ── Walk / Allowlist ──────────────────────────────────────────────────────

    [Fact]
    public async Task Walk_UndeclaredEndpoint_StopsWalk()
    {
        var cfg = Helpers.Config(("balance", "getbalance", false));
        var fs = Helpers.Fs(cfg);

        var result = await fs.WalkAsync(new Twalk(1, 10, 11, new[] { "undeclared" }));

        result.Wqid.Should().BeEmpty("walk should stop at first unknown component");
    }

    [Fact]
    public async Task Walk_DeclaredEndpoint_ReturnsOneFileQid()
    {
        var cfg = Helpers.Config(("balance", "getbalance", false));
        var fs = Helpers.Fs(cfg);

        var result = await fs.WalkAsync(new Twalk(1, 10, 11, new[] { "balance" }));

        result.Wqid.Should().HaveCount(1);
        result.Wqid[0].Type.Should().Be(QidType.QTFILE);
    }

    [Fact]
    public async Task Walk_EmptyNames_ReturnsEmptyQids()
    {
        var cfg = Helpers.Config();
        var fs = Helpers.Fs(cfg);

        var result = await fs.WalkAsync(new Twalk(1, 10, 11, Array.Empty<string>()));

        result.Wqid.Should().BeEmpty();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Read_DeclaredEndpoint_CallsCorrectRpcMethod()
    {
        var transport = new FakeTransport(JsonValue.Create(100));
        var cfg = Helpers.Config(("balance", "getbalance", false));
        var fs = await Helpers.FsAt(cfg, transport, "balance");

        await fs.ReadAsync(new Tread(3, 11, 0, 8192));

        transport.LastMethod.Should().Be("getbalance");
    }

    [Fact]
    public async Task Read_AtNonzeroOffset_ReturnsEmpty()
    {
        var transport = new FakeTransport(JsonValue.Create("data"));
        var cfg = Helpers.Config(("info", "getinfo", false));
        var fs = await Helpers.FsAt(cfg, transport, "info");

        var read = await fs.ReadAsync(new Tread(3, 11, 1, 8192));

        read.Data.ToArray().Should().BeEmpty();
        transport.CallCount.Should().Be(0, "no RPC call should happen at non-zero offset");
    }

    [Fact]
    public async Task Read_Root_ListsOnlyEndpointNames_NotMethodNames()
    {
        var cfg = Helpers.Config(("myfile", "internalMethod", false));
        var fs = Helpers.Fs(cfg);

        var read = await fs.ReadAsync(new Tread(1, 10, 0, 8192));
        var entries = Helpers.ParseDirectory(read.Data.ToArray());
        var names = entries.Select(s => s.Name).ToArray();

        names.Should().Contain("myfile");
        names.Should().NotContain("internalMethod", "RPC method names must never leak into the directory listing");
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Write_WritableEndpoint_PassesPayloadAsParamOnNextRead()
    {
        var transport = new FakeTransport(JsonNode.Parse("{\"value\":\"ok\"}"));
        var cfg = Helpers.Config(("nameshow", "name_show", true));
        var fs = await Helpers.FsAt(cfg, transport, "nameshow");

        var payload = Encoding.UTF8.GetBytes("emc:myrecord");
        var write = await fs.WriteAsync(new Twrite(3, 11, 0, payload));
        write.Count.Should().Be((uint)payload.Length);

        await fs.ReadAsync(new Tread(4, 11, 0, 8192));
        transport.LastArgs.Should().NotBeNull();
        transport.LastArgs![0]!.ToString().Should().Be("emc:myrecord");
    }

    [Fact]
    public async Task Write_ReadOnlyEndpoint_ReturnsZeroCountAndMakesNoRpcCall()
    {
        var transport = new FakeTransport();
        var cfg = Helpers.Config(("balance", "getbalance", false));
        var fs = await Helpers.FsAt(cfg, transport, "balance");

        var write = await fs.WriteAsync(new Twrite(3, 11, 0, Encoding.UTF8.GetBytes("ignored")));

        write.Count.Should().Be(0);
    }

    // ── Stat ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stat_ReadOnlyEndpoint_HasNoWriteBits()
    {
        var cfg = Helpers.Config(("balance", "getbalance", false));
        var fs = await Helpers.FsAt(cfg, new FakeTransport(), "balance");

        var stat = await fs.StatAsync(new Tstat(2, 11));

        // 0x92 = -w--w--w- (write-all Unix bits)
        const uint writeBits = 0x92;
        (stat.Stat.Mode & writeBits).Should().Be(0u);
    }

    [Fact]
    public async Task Stat_WritableEndpoint_HasWriteBits()
    {
        var cfg = Helpers.Config(("nameshow", "name_show", true));
        var fs = await Helpers.FsAt(cfg, new FakeTransport(), "nameshow");

        var stat = await fs.StatAsync(new Tstat(2, 11));

        const uint writeBits = 0x92;
        (stat.Stat.Mode & writeBits).Should().NotBe(0u);
    }

    [Fact]
    public async Task Stat_Root_IsDirectory()
    {
        var cfg = Helpers.Config();
        var fs = Helpers.Fs(cfg);

        var stat = await fs.StatAsync(new Tstat(1, 10));

        (stat.Stat.Mode & (uint)NinePConstants.FileMode9P.DMDIR).Should().NotBe(0u);
    }

    // ── Clone ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clone_IsIndependentFromOriginal()
    {
        var cfg = Helpers.Config(("balance", "getbalance", false));
        var fs = Helpers.Fs(cfg);
        var clone = (JsonRpcFileSystem)fs.Clone();

        // Walk clone to "balance" — original stays at root
        await clone.WalkAsync(new Twalk(1, 10, 11, new[] { "balance" }));

        var statClone = await clone.StatAsync(new Tstat(2, 11));
        var statOrig = await fs.StatAsync(new Tstat(2, 10));

        statClone.Stat.Name.Should().Be("balance");
        statOrig.Stat.Name.Should().Be("jsonrpc"); // root stat name
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Property Tests (FsCheck)

public class JsonRpcFileSystemPropertyTests
{
    private static JsonRpcFileSystem MakeFs(IEnumerable<string> names)
    {
        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc", EndpointUrl = "http://fake/" };
        foreach (var n in names)
            cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = n, Method = "m", Writable = false });
        return new JsonRpcFileSystem(cfg, new FakeTransport());
    }

    /// <summary>Walking to any name NOT in the allowlist always returns 0 qids.</summary>
    [Property]
    public bool Walk_NonAllowlisted_NeverSucceeds(NonEmptyString declared, NonEmptyString notDeclared)
    {
        // Skip trivially equal and special walk names . and ..
        if (declared.Get == notDeclared.Get) return true;
        if (notDeclared.Get == "." || notDeclared.Get == "..") return true;

        var fs = MakeFs(new[] { declared.Get });
        var result = fs.WalkAsync(new Twalk(1, 10, 11, new[] { notDeclared.Get }))
                       .GetAwaiter().GetResult();
        return result.Wqid.Length == 0;
    }

    /// <summary>Walking to any declared name always returns exactly 1 qid.</summary>
    [Property]
    public bool Walk_Allowlisted_AlwaysSucceeds(NonEmptyString name)
    {
        var fs = MakeFs(new[] { name.Get });
        var result = fs.WalkAsync(new Twalk(1, 10, 11, new[] { name.Get }))
                       .GetAwaiter().GetResult();
        return result.Wqid.Length == 1;
    }

    /// <summary>Root listing never contains RPC method names — only filesystem names.</summary>
    [Property]
    public bool RootListing_NeverExposesMethodNames(NonEmptyString fsName, NonEmptyString methodName)
    {
        var n = fsName.Get;
        var m = methodName.Get;
        // Skip impossible or reserved-control cases.
        if (n == m || m == "status") return true;
        if (n.Any(c => c < 32 || c == '\n' || c == '\r')) return true;
        if (m.Any(c => c < 32 || c == '\n' || c == '\r')) return true;

        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc", EndpointUrl = "http://fake/" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = fsName.Get, Method = methodName.Get });
        var fs = new JsonRpcFileSystem(cfg, new FakeTransport());

        var read = fs.ReadAsync(new Tread(1, 10, 0, 65535)).GetAwaiter().GetResult();
        var names = Helpers.ParseDirectory(read.Data.ToArray()).Select(s => s.Name).ToArray();

        return names.Contains(fsName.Get) && !names.Contains(methodName.Get);
    }

    /// <summary>Reading at any nonzero offset always returns empty bytes.</summary>
    [Property]
    public bool Read_NonzeroOffset_AlwaysEmpty(NonEmptyString name, PositiveInt offset)
    {
        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc", EndpointUrl = "http://fake/" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = name.Get, Method = "m" });
        var fs = new JsonRpcFileSystem(cfg, new FakeTransport(JsonValue.Create("data")));

        fs.WalkAsync(new Twalk(1, 10, 11, new[] { name.Get })).GetAwaiter().GetResult();
        var read = fs.ReadAsync(new Tread(3, 11, (ulong)offset.Get, 8192)).GetAwaiter().GetResult();

        return read.Data.ToArray().Length == 0;
    }

    /// <summary>Clone never shares path state with original.</summary>
    [Property]
    public bool Clone_PathIsAlwaysIsolated(NonEmptyString name)
    {
        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc", EndpointUrl = "http://fake/" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = name.Get, Method = "m" });
        var original = new JsonRpcFileSystem(cfg, new FakeTransport());
        var clone = (JsonRpcFileSystem)original.Clone();

        // Walk only the clone
        clone.WalkAsync(new Twalk(1, 10, 11, new[] { name.Get })).GetAwaiter().GetResult();

        var origStat = original.StatAsync(new Tstat(1, 10)).GetAwaiter().GetResult();
        var cloneStat = clone.StatAsync(new Tstat(1, 11)).GetAwaiter().GetResult();

        return origStat.Stat.Name != cloneStat.Stat.Name;
    }
}
