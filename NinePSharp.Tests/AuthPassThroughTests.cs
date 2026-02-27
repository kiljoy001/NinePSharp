using NinePSharp.Constants;
using System.Security.Cryptography.X509Certificates;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Moq;
using Xunit;

namespace NinePSharp.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Test doubles

internal class NullLoggerAuth : ILogger<NinePFSDispatcher>
{
    public IDisposable? BeginScope<T>(T state) where T : notnull => null;
    public bool IsEnabled(LogLevel l) => false;
    public void Log<T>(LogLevel l, EventId e, T s, Exception? ex, Func<T, Exception?, string> f) { }
}

/// <summary>
/// Spy backend — records the credentials passed to GetFileSystem(credentials).
/// </summary>
internal class SpyBackend : IProtocolBackend
{
    private readonly ILuxVaultService _vault = new LuxVaultService();
    public string? LastCredentials { get; private set; } = "NOT_CALLED";
    public int GetFileSystemCallCount { get; private set; }

    public string Name => "Spy";
    public string MountPath => "/spy";

    public Task InitializeAsync(Microsoft.Extensions.Configuration.IConfiguration c) => Task.CompletedTask;

    public INinePFileSystem GetFileSystem(X509Certificate2? certificate = null)
    {
        GetFileSystemCallCount++;
        LastCredentials = null;
        return new MockFileSystem(_vault);
    }

    public INinePFileSystem GetFileSystem(System.Security.SecureString? credentials, X509Certificate2? certificate = null)
    {
        GetFileSystemCallCount++;
        if (credentials != null)
        {
            IntPtr ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(credentials);
            try {
                LastCredentials = System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
            } finally {
                System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
        else {
            LastCredentials = null;
        }
        return new MockFileSystem(_vault);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers

internal static class Auth
{
    private static IClusterManager CreateMockClusterManager()
    {
        return new Mock<IClusterManager>().Object;
    }

    public static INinePFSDispatcher Dispatcher(SpyBackend backend)
        => new NinePFSDispatcher(new NullLoggerAuth(), new[] { backend }, CreateMockClusterManager());

    public static async Task<uint> DoTauth(INinePFSDispatcher d, uint afid = 42, ushort tag = 1)
    {
        var tauth = new Tauth(tag, afid, "root", "/spy");
        await d.DispatchAsync(NinePMessage.NewMsgTauth(tauth), NinePDialect.NineP2000);
        return afid;
    }

    public static async Task WriteCredentials(INinePFSDispatcher d, uint afid, string creds, ushort tag = 2)
    {
        var data = Encoding.UTF8.GetBytes(creds);
        var twrite = new Twrite(tag, afid, 0, data);
        await d.DispatchAsync(NinePMessage.NewMsgTwrite(twrite), NinePDialect.NineP2000);
    }

    public static async Task<object> DoTattach(INinePFSDispatcher d, uint afid, uint fid = 100, ushort tag = 3)
    {
        var tattach = new Tattach(tag, fid, afid, "root", "/spy");
        return await d.DispatchAsync(NinePMessage.NewMsgTattach(tattach), NinePDialect.NineP2000);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Unit Tests

public class AuthPassThroughDispatcherTests
{
    // ── Tauth ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tauth_Returns_Rauth_With_QTAUTH_Qid()
    {
        var d = Auth.Dispatcher(new SpyBackend());
        var tauth = new Tauth(1, 42, "root", "/spy");

        var response = await d.DispatchAsync(NinePMessage.NewMsgTauth(tauth), NinePDialect.NineP2000);

        response.Should().BeOfType<Rauth>();
        var rauth = (Rauth)response;
        rauth.Tag.Should().Be(1);
        rauth.Aqid.Type.Should().Be(QidType.QTAUTH);
        rauth.Aqid.Path.Should().Be(42); // afid echoed back in path
    }

    [Fact]
    public async Task Tauth_With_DifferentAfids_Creates_Separate_Buffers()
    {
        var spy = new SpyBackend();
        var d = Auth.Dispatcher(spy);

        await Auth.DoTauth(d, afid: 10);
        await Auth.DoTauth(d, afid: 20);

        // Write different credentials to each
        await Auth.WriteCredentials(d, 10, "user:passA");
        await Auth.WriteCredentials(d, 20, "user:passB");

        // Attach using afid=10
        await Auth.DoTattach(d, afid: 10, fid: 100);
        spy.LastCredentials.Should().Be("user:passA");

        // Attach using afid=20
        await Auth.DoTattach(d, afid: 20, fid: 101);
        spy.LastCredentials.Should().Be("user:passB");
    }

    // ── Twrite to auth fid ────────────────────────────────────────────────────

    [Fact]
    public async Task Twrite_To_AuthFid_Accumulates_Credentials()
    {
        var spy = new SpyBackend();
        var d = Auth.Dispatcher(spy);
        await Auth.DoTauth(d, afid: 42);

        // Write in two chunks — simulates streaming
        await Auth.WriteCredentials(d, 42, "user:");
        await Auth.WriteCredentials(d, 42, "secret");

        await Auth.DoTattach(d, afid: 42);
        spy.LastCredentials.Should().Be("user:secret");
    }

    [Fact]
    public async Task Twrite_To_AuthFid_Returns_Rwrite_With_Full_Count()
    {
        var d = Auth.Dispatcher(new SpyBackend());
        await Auth.DoTauth(d, afid: 42);

        var data = Encoding.UTF8.GetBytes("mypassword");
        var twrite = new Twrite(2, 42, 0, data);
        var response = await d.DispatchAsync(NinePMessage.NewMsgTwrite(twrite), NinePDialect.NineP2000);

        response.Should().BeOfType<Rwrite>();
        ((Rwrite)response).Count.Should().Be((uint)data.Length);
    }

    [Fact]
    public async Task Twrite_To_NonAuthFid_Without_Attach_Returns_Error()
    {
        var d = Auth.Dispatcher(new SpyBackend());
        // Write to fid 99 which has never been attached or authed
        var data = Encoding.UTF8.GetBytes("data");
        var twrite = new Twrite(1, 99, 0, data);
        var response = await d.DispatchAsync(NinePMessage.NewMsgTwrite(twrite), NinePDialect.NineP2000);

        response.Should().BeOfType<Rerror>();
    }

    // ── Tattach ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tattach_With_AuthFid_Passes_Credentials_To_Backend()
    {
        var spy = new SpyBackend();
        var d = Auth.Dispatcher(spy);

        await Auth.DoTauth(d, afid: 42);
        await Auth.WriteCredentials(d, 42, "rpcuser:rpcpass");
        var response = await Auth.DoTattach(d, afid: 42);

        response.Should().BeOfType<Rattach>();
        spy.LastCredentials.Should().Be("rpcuser:rpcpass");
    }

    [Fact]
    public async Task Tattach_Without_AuthFid_Passes_Null_Credentials()
    {
        var spy = new SpyBackend();
        var d = Auth.Dispatcher(spy);

        // NOFID = no auth used
        var tattach = new Tattach(1, 100, NinePConstants.NoFid, "root", "/spy");
        var response = await d.DispatchAsync(NinePMessage.NewMsgTattach(tattach), NinePDialect.NineP2000);

        response.Should().BeOfType<Rattach>();
        spy.LastCredentials.Should().BeNull();
    }

    [Fact]
    public async Task Tattach_With_Empty_AuthFid_Passes_Null_Not_EmptyString()
    {
        var spy = new SpyBackend();
        var d = Auth.Dispatcher(spy);

        // Auth fid created but nothing written to it
        await Auth.DoTauth(d, afid: 42);
        await Auth.DoTattach(d, afid: 42);

        // Empty buffer → null credentials (not "")
        spy.LastCredentials.Should().BeNull();
    }

    [Fact]
    public async Task Tattach_Drains_AuthFid_Buffer_So_Second_Attach_Gets_Null()
    {
        var spy = new SpyBackend();
        var d = Auth.Dispatcher(spy);

        await Auth.DoTauth(d, afid: 42);
        await Auth.WriteCredentials(d, 42, "secret");

        // First attach consumes the buffer
        await Auth.DoTattach(d, afid: 42, fid: 100);
        spy.LastCredentials.Should().Be("secret");

        // Second attach with same afid — buffer already removed, NOFID behaviour
        await Auth.DoTattach(d, afid: 42, fid: 101);
        spy.LastCredentials.Should().BeNull();
    }

    // ── Tclunk on auth fid ────────────────────────────────────────────────────

    [Fact]
    public async Task Tclunk_AuthFid_Cleans_Up_Buffer()
    {
        var spy = new SpyBackend();
        var d = Auth.Dispatcher(spy);

        await Auth.DoTauth(d, afid: 42);
        await Auth.WriteCredentials(d, 42, "secret");

        // Clunk the auth fid before attaching
        var tclunk = new Tclunk(99, 42);
        var clunkResp = await d.DispatchAsync(NinePMessage.NewMsgTclunk(tclunk), NinePDialect.NineP2000);
        clunkResp.Should().BeOfType<Rclunk>();

        // Now attach — buffer is gone, credentials should be null
        await Auth.DoTattach(d, afid: 42, fid: 100);
        spy.LastCredentials.Should().BeNull();
    }

    // ── aname backend selection ───────────────────────────────────────────────

    [Fact]
    public async Task Tattach_Unknown_Aname_Returns_Error()
    {
        var spy = new SpyBackend();
        var d = Auth.Dispatcher(spy);

        var tattach = new Tattach(1, 100, NinePConstants.NoFid, "root", "/nonexistent");
        var response = await d.DispatchAsync(NinePMessage.NewMsgTattach(tattach), NinePDialect.NineP2000);

        response.Should().BeOfType<Rerror>();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Property Tests

public class AuthPassThroughPropertyTests
{
    private static readonly ILuxVaultService _vault = new LuxVaultService();

    private static IClusterManager CreateMockClusterManager()
    {
        return new Mock<IClusterManager>().Object;
    }

    private static INinePFSDispatcher MakeDispatcher(out SpyBackend spy)
    {
        spy = new SpyBackend();
        return new NinePFSDispatcher(new NullLoggerAuth(), new[] { spy }, CreateMockClusterManager());
    }

    /// <summary>
    /// Any non-empty credential string written to an auth fid
    /// is always forwarded verbatim to the backend.
    /// </summary>
    [Property]
    public bool Credentials_Always_Forwarded_Verbatim(NonEmptyString creds)
    {
        var c = creds.Get;
        if (c.Any(ch => ch < 32)) return true; // skip control chars

        var d = MakeDispatcher(out var spy);
        Auth.DoTauth(d, 42).Sync();
        Auth.WriteCredentials(d, 42, c).Sync();
        Auth.DoTattach(d, 42).Sync();

        return spy.LastCredentials == c;
    }

    /// <summary>
    /// Credentials written across multiple Twrite chunks are
    /// always concatenated and forwarded as one string.
    /// </summary>
    [Property]
    public bool MultiChunk_Write_Concatenated(NonEmptyString part1, NonEmptyString part2)
    {
        var p1 = part1.Get;
        var p2 = part2.Get;
        if (p1.Any(c => c < 32) || p2.Any(c => c < 32)) return true;

        var d = MakeDispatcher(out var spy);
        Auth.DoTauth(d, 42).Sync();
        Auth.WriteCredentials(d, 42, p1).Sync();
        Auth.WriteCredentials(d, 42, p2).Sync();
        Auth.DoTattach(d, 42).Sync();

        return spy.LastCredentials == p1 + p2;
    }

    /// <summary>
    /// After Tattach consumes an auth fid's buffer,
    /// a subsequent Tattach with the same afid always gets null credentials.
    /// </summary>
    [Property]
    public bool AuthFid_Buffer_Consumed_After_First_Attach(NonEmptyString creds)
    {
        var c = creds.Get;
        if (c.Any(ch => ch < 32)) return true;

        var d = MakeDispatcher(out var spy);
        Auth.DoTauth(d, 42).Sync();
        Auth.WriteCredentials(d, 42, c).Sync();
        Auth.DoTattach(d, 42, fid: 100).Sync();

        // Second attach — buffer already drained
        Auth.DoTattach(d, 42, fid: 101).Sync();

        return spy.LastCredentials == null;
    }

    /// <summary>
    /// Any Twrite to an auth fid always returns Rwrite with full byte count.
    /// </summary>
    [Property]
    public bool Write_To_AuthFid_Always_Returns_Full_Count(NonEmptyString creds)
    {
        var c = creds.Get;
        if (c.Any(ch => ch < 32)) return true;

        var d = MakeDispatcher(out var _);
        Auth.DoTauth(d, 42).Sync();

        var data = Encoding.UTF8.GetBytes(c);
        var twrite = new Twrite(1, 42, 0, data);
        var resp = d.DispatchAsync(NinePMessage.NewMsgTwrite(twrite), NinePDialect.NineP2000).Sync();

        return resp is Rwrite rw && rw.Count == (uint)data.Length;
    }

    /// <summary>
    /// Attaching without an auth fid (NOFID) always produces null credentials.
    /// </summary>
    [Property]
    public bool NoAfid_Always_Produces_Null_Credentials(PositiveInt tag)
    {
        var d = MakeDispatcher(out var spy);
        var tattach = new Tattach((ushort)(tag.Get % 65535 + 1), 100, NinePConstants.NoFid, "root", "/spy");
        d.DispatchAsync(NinePMessage.NewMsgTattach(tattach), NinePDialect.NineP2000).Sync();

        return spy.LastCredentials == null;
    }
}
