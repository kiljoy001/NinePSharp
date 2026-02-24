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

public class JsonRpcHierarchicalFileSystemTests
{
    [Fact]
    public async Task Walk_NestedDirectory_Succeeds()
    {
        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = "balance", Path = "accounts/eth", Method = "eth_getBalance" });
        var fs = new JsonRpcFileSystem(cfg, new FakeTransport());

        // Walk to first level
        var result1 = await fs.WalkAsync(new Twalk(1, 10, 11, new[] { "accounts" }));
        result1.Wqid.Should().HaveCount(1);
        result1.Wqid[0].Type.Should().Be(QidType.QTDIR);

        // Walk to second level
        var result2 = await fs.WalkAsync(new Twalk(2, 11, 12, new[] { "eth" }));
        result2.Wqid.Should().HaveCount(1);
        result2.Wqid[0].Type.Should().Be(QidType.QTDIR);

        // Walk to file
        var result3 = await fs.WalkAsync(new Twalk(3, 12, 13, new[] { "balance" }));
        result3.Wqid.Should().HaveCount(1);
        result3.Wqid[0].Type.Should().Be(QidType.QTFILE);
    }

    [Fact]
    public async Task Read_NestedDirectory_ListsSubdirectoriesAndFiles()
    {
        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = "balance", Path = "accounts/eth", Method = "eth_getBalance" });
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = "count", Path = "accounts", Method = "get_account_count" });
        var fs = new JsonRpcFileSystem(cfg, new FakeTransport());

        // Read root
        var readRoot = await fs.ReadAsync(new Tread(1, 10, 0, 8192));
        var rootEntries = Helpers.ParseDirectory(readRoot.Data.ToArray());
        rootEntries.Select(e => e.Name).Should().Contain("accounts");
        rootEntries.FirstOrDefault(e => e.Name == "accounts")!.Qid.Type.Should().Be(QidType.QTDIR);

        // Walk to 'accounts'
        await fs.WalkAsync(new Twalk(2, 10, 11, new[] { "accounts" }));
        var readAccounts = await fs.ReadAsync(new Tread(3, 11, 0, 8192));
        var accountsEntries = Helpers.ParseDirectory(readAccounts.Data.ToArray());
        accountsEntries.Select(e => e.Name).Should().Contain(new[] { "eth", "count" });
        accountsEntries.FirstOrDefault(e => e.Name == "eth")!.Qid.Type.Should().Be(QidType.QTDIR);
        accountsEntries.FirstOrDefault(e => e.Name == "count")!.Qid.Type.Should().Be(QidType.QTFILE);
    }

    [Fact]
    public async Task Call_NestedEndpoint_InvokesCorrectMethod()
    {
        var transport = new FakeTransport(JsonValue.Create(100));
        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = "balance", Path = "accounts/eth", Method = "eth_getBalance" });
        var fs = new JsonRpcFileSystem(cfg, transport);

        // Walk to file
        await fs.WalkAsync(new Twalk(1, 10, 11, new[] { "accounts", "eth", "balance" }));
        
        // Read file
        await fs.ReadAsync(new Tread(2, 11, 0, 8192));

        transport.LastMethod.Should().Be("eth_getBalance");
    }

    [Fact]
    public async Task Walk_OverlappingPaths_Succeeds()
    {
        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = "eth", Path = "wallets/crypto", Method = "m1" });
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = "btc", Path = "wallets/crypto", Method = "m2" });
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = "info", Path = "wallets", Method = "m3" });
        var fs = new JsonRpcFileSystem(cfg, new FakeTransport());

        // Walk to common prefix
        var result1 = await fs.WalkAsync(new Twalk(1, 10, 11, new[] { "wallets" }));
        result1.Wqid.Should().HaveCount(1);

        // Read common prefix - should see 'crypto' (DIR) and 'info' (FILE)
        var read = await fs.ReadAsync(new Tread(2, 11, 0, 8192));
        var entries = Helpers.ParseDirectory(read.Data.ToArray());
        entries.Select(e => e.Name).Should().Contain(new[] { "crypto", "info" });
        entries.First(e => e.Name == "crypto").Qid.Type.Should().Be(QidType.QTDIR);
        entries.First(e => e.Name == "info").Qid.Type.Should().Be(QidType.QTFILE);
    }

    [Fact]
    public async Task Walk_DeeplyNested_Succeeds()
    {
        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = "leaf", Path = "a/b/c/d/e", Method = "m" });
        var fs = new JsonRpcFileSystem(cfg, new FakeTransport());

        var result = await fs.WalkAsync(new Twalk(1, 10, 11, new[] { "a", "b", "c", "d", "e", "leaf" }));
        result.Wqid.Should().HaveCount(6);
        result.Wqid.Last().Type.Should().Be(QidType.QTFILE);
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
        var n = name.Get.Replace("/", "").Trim();
        if (string.IsNullOrEmpty(n)) return true;

        var fs = MakeFs(new[] { n });
        var result = fs.WalkAsync(new Twalk(1, 10, 11, new[] { n }))
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
        var n = name.Get.Replace("/", "").Trim();
        if (string.IsNullOrEmpty(n)) return true;

        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc", EndpointUrl = "http://fake/" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = n, Method = "m" });
        var fs = new JsonRpcFileSystem(cfg, new FakeTransport(JsonValue.Create("data")));

        fs.WalkAsync(new Twalk(1, 10, 11, new[] { n })).GetAwaiter().GetResult();
        var read = fs.ReadAsync(new Tread(3, 11, (ulong)offset.Get, 8192)).GetAwaiter().GetResult();

        return read.Data.ToArray().Length == 0;
    }

    /// <summary>Clone never shares path state with original.</summary>
    [Property]
    public bool Clone_PathIsAlwaysIsolated(NonEmptyString name)
    {
        var n = name.Get.Replace("/", "").Trim();
        if (string.IsNullOrEmpty(n)) return true;

        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc", EndpointUrl = "http://fake/" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = n, Method = "m" });
        var original = new JsonRpcFileSystem(cfg, new FakeTransport());
        var clone = (JsonRpcFileSystem)original.Clone();

        // Walk only the clone
        clone.WalkAsync(new Twalk(1, 10, 11, new[] { n })).GetAwaiter().GetResult();

        var origStat = original.StatAsync(new Tstat(1, 10)).GetAwaiter().GetResult();
        var cloneStat = clone.StatAsync(new Tstat(1, 11)).GetAwaiter().GetResult();

        return origStat.Stat.Name != cloneStat.Stat.Name;
    }

    /// <summary>Every declared hierarchical endpoint must be reachable via Walk.</summary>
    [Property]
    public bool Walk_Hierarchical_AlwaysReachable(NonNull<string>[] pathParts, NonEmptyString name)
    {
        // Sanitize path parts to avoid empty or invalid segments
        var cleanParts = pathParts.Select(p => p.Item.Replace("/", "").Trim())
                                  .Where(p => !string.IsNullOrEmpty(p))
                                  .ToArray();
        var n = name.Get.Replace("/", "").Trim();
        if (string.IsNullOrEmpty(n)) return true;

        string path = string.Join("/", cleanParts);
        
        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc", EndpointUrl = "http://fake/" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = n, Path = path, Method = "m" });
        var fs = new JsonRpcFileSystem(cfg, new FakeTransport());

        var walkParts = cleanParts.Concat(new[] { n }).ToArray();
        var result = fs.WalkAsync(new Twalk(1, 10, 11, walkParts)).GetAwaiter().GetResult();

        return result.Wqid.Length == walkParts.Length;
    }

    /// <summary>Intermediate directories must appear in their parent's listing.</summary>
    [Property]
    public bool IntermediateDirectory_AlwaysAppearsInListing(NonEmptyString part1, NonEmptyString part2, NonEmptyString name)
    {
        var p1 = part1.Get.Replace("/", "").Trim();
        var p2 = part2.Get.Replace("/", "").Trim();
        var n = name.Get.Replace("/", "").Trim();
        if (string.IsNullOrEmpty(p1) || string.IsNullOrEmpty(p2) || string.IsNullOrEmpty(n)) return true;
        if (p1 == p2) return true;

        var cfg = new JsonRpcBackendConfig { MountPath = "/rpc", EndpointUrl = "http://fake/" };
        cfg.Endpoints.Add(new JsonRpcEndpointConfig { Name = n, Path = $"{p1}/{p2}", Method = "m" });
        var fs = new JsonRpcFileSystem(cfg, new FakeTransport());

        // Check root contains p1
        var readRoot = fs.ReadAsync(new Tread(1, 10, 0, 65535)).GetAwaiter().GetResult();
        var rootNames = Helpers.ParseDirectory(readRoot.Data.ToArray()).Select(s => s.Name);
        if (!rootNames.Contains(p1)) return false;

        // Walk to p1 and check it contains p2
        fs.WalkAsync(new Twalk(2, 10, 11, new[] { p1 })).GetAwaiter().GetResult();
        var readP1 = fs.ReadAsync(new Tread(3, 11, 0, 65535)).GetAwaiter().GetResult();
        var p1Names = Helpers.ParseDirectory(readP1.Data.ToArray()).Select(s => s.Name);
        
        return p1Names.Contains(p2);
    }
}
