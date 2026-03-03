using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Constants;
using NinePSharp.Examples;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using Xunit;

namespace NinePSharp.Tests;

/// <summary>
/// Tests for auth file I/O: Tauth creates auth fid, Twrite/Tread exchange
/// auth protocol data through the handler, and Tattach uses the credentials.
/// Per auth(2) and 9front factotum semantics.
/// </summary>
public class AuthFileIoTests
{
    /// <summary>
    /// Test handler that records auth writes and returns canned auth reads.
    /// </summary>
    private class AuthTestHandler : InMemoryHandler
    {
        public byte[]? LastAuthWrite { get; private set; }
        public uint LastAuthWriteAfid { get; private set; }
        public byte[] AuthReadResponse { get; set; } = Encoding.UTF8.GetBytes("v.2 p9any proto=p9sk1");

        public override Task<byte[]> AuthReadAsync(uint afid, ulong offset, uint count, CancellationToken ct)
        {
            if (offset >= (ulong)AuthReadResponse.Length)
                return Task.FromResult(Array.Empty<byte>());

            int start = (int)offset;
            int len = (int)Math.Min(count, (uint)(AuthReadResponse.Length - start));
            var data = new byte[len];
            Array.Copy(AuthReadResponse, start, data, 0, len);
            return Task.FromResult(data);
        }

        public override Task<uint> AuthWriteAsync(uint afid, ulong offset, byte[] data, CancellationToken ct)
        {
            LastAuthWriteAfid = afid;
            // Must copy — dispatcher zeroes the original array after this returns (security)
            LastAuthWrite = (byte[])data.Clone();
            return Task.FromResult((uint)data.Length);
        }
    }

    private static NinePFSDispatcher CreateDispatcher(INinePRequestHandler handler)
    {
        return new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, handler);
    }

    [Fact]
    public async Task Tauth_Creates_Auth_Fid()
    {
        var handler = new AuthTestHandler();
        var dispatcher = CreateDispatcher(handler);

        var response = await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTauth(new Tauth(1, 10, "scott", "/")),
            NinePDialect.NineP2000);

        response.Should().BeOfType<Rauth>();
        var rauth = (Rauth)response;
        rauth.Aqid.Type.Should().Be(QidType.QTAUTH);
    }

    [Fact]
    public async Task Tread_On_Auth_Fid_Returns_Handler_Data()
    {
        var handler = new AuthTestHandler();
        handler.AuthReadResponse = Encoding.UTF8.GetBytes("challenge-data-here");
        var dispatcher = CreateDispatcher(handler);

        // Create auth fid
        await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTauth(new Tauth(1, 10, "scott", "/")),
            NinePDialect.NineP2000);

        // Read from auth fid — should NOT fail with "Unknown FID"
        var readResponse = await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTread(new Tread(2, 10, 0, 1024)),
            NinePDialect.NineP2000);

        readResponse.Should().BeOfType<Rread>();
        var rread = (Rread)readResponse;
        Encoding.UTF8.GetString(rread.Data.Span).Should().Be("challenge-data-here");
    }

    [Fact]
    public async Task Twrite_On_Auth_Fid_Delegates_To_Handler()
    {
        var handler = new AuthTestHandler();
        var dispatcher = CreateDispatcher(handler);

        // Create auth fid
        await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTauth(new Tauth(1, 10, "scott", "/")),
            NinePDialect.NineP2000);

        // Write auth credentials
        string secret = "proto=p9any role=client";
        var writeResponse = await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTwrite(new Twrite(2, 10, 0, Encoding.UTF8.GetBytes(secret))),
            NinePDialect.NineP2000);

        writeResponse.Should().BeOfType<Rwrite>();
        var rwrite = (Rwrite)writeResponse;
        rwrite.Count.Should().Be((uint)Encoding.UTF8.GetByteCount(secret));

        // Verify handler was called
        handler.LastAuthWriteAfid.Should().Be(10u);
        handler.LastAuthWrite.Should().NotBeNull();
        Encoding.UTF8.GetString(handler.LastAuthWrite!).Should().Be(secret);
    }

    [Fact]
    public async Task Full_Auth_RoundTrip_Tauth_Write_Read_Attach()
    {
        var handler = new AuthTestHandler();
        handler.AuthReadResponse = Encoding.UTF8.GetBytes("auth-ok");
        var dispatcher = CreateDispatcher(handler);

        // 1. Tauth - create auth fid
        var authResp = await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTauth(new Tauth(1, 10, "scott", "/")),
            NinePDialect.NineP2000);
        authResp.Should().BeOfType<Rauth>();

        // 2. Twrite to auth fid - send credentials
        string creds = "rpcuser:rpcpass";
        var writeResp = await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTwrite(new Twrite(2, 10, 0, Encoding.UTF8.GetBytes(creds))),
            NinePDialect.NineP2000);
        writeResp.Should().BeOfType<Rwrite>();

        // 3. Tread from auth fid - read response
        var readResp = await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTread(new Tread(3, 10, 0, 256)),
            NinePDialect.NineP2000);
        readResp.Should().BeOfType<Rread>();
        Encoding.UTF8.GetString(((Rread)readResp).Data.Span).Should().Be("auth-ok");

        // 4. Tattach with auth fid
        var attachResp = await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTattach(new Tattach(4, 1, 10, "scott", "/")),
            NinePDialect.NineP2000);
        attachResp.Should().BeOfType<Rattach>();
    }

    [Fact]
    public async Task Tread_On_Regular_Fid_Still_Requires_Open()
    {
        var handler = new InMemoryHandler();
        handler.AddFile("test.txt", "hello");
        var dispatcher = CreateDispatcher(handler);

        // Attach
        await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "user", "/")),
            NinePDialect.NineP2000);

        // Walk to file
        await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTwalk(new Twalk(2, 1, 2, new[] { "test.txt" })),
            NinePDialect.NineP2000);

        // Read WITHOUT open — should fail
        var readResp = await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTread(new Tread(3, 2, 0, 1024)),
            NinePDialect.NineP2000);

        readResp.Should().BeOfType<Rerror>("reading a regular fid without open should fail");
    }

    [Fact]
    public async Task Tclunk_On_Auth_Fid_Works()
    {
        var handler = new AuthTestHandler();
        var dispatcher = CreateDispatcher(handler);

        // Create auth fid
        await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTauth(new Tauth(1, 10, "scott", "/")),
            NinePDialect.NineP2000);

        // Clunk auth fid
        var clunkResp = await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTclunk(new Tclunk(2, 10)),
            NinePDialect.NineP2000);

        clunkResp.Should().BeOfType<Rclunk>();

        // Read after clunk should fail
        var readResp = await dispatcher.DispatchAsync(
            "s1",
            NinePMessage.NewMsgTread(new Tread(3, 10, 0, 256)),
            NinePDialect.NineP2000);

        readResp.Should().BeOfType<Rerror>("auth fid should be gone after clunk");
    }
}
