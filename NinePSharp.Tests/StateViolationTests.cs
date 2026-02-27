using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Parser;
using NinePSharp.Interfaces;
using NinePSharp.Server.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using FluentAssertions;

namespace NinePSharp.Tests;

/// <summary>
/// Tests that verify FID state machine violations are properly rejected
/// These tests target surviving mutants in NinePFSDispatcher that don't check FID validity
/// </summary>
public class StateViolationTests
{
    private readonly NinePFSDispatcher _dispatcher;

    public StateViolationTests()
    {
        var vault = new LuxVaultService();
        var cluster = new Mock<IClusterManager>().Object;
        var mockBackend = new Mock<IProtocolBackend>();
        mockBackend.Setup(b => b.MountPath).Returns("/mock");
        mockBackend.Setup(b => b.GetFileSystem(It.IsAny<System.Security.SecureString>(), It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2>())).Returns(new MockFileSystem(vault));
        mockBackend.Setup(b => b.GetFileSystem(It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2>())).Returns(new MockFileSystem(vault));
        
        _dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { mockBackend.Object }, cluster);
    }

    private async Task<object> Dispatch(ISerializable msg)
    {
        // Wrap in NinePMessage for DispatchAsync
        var pmsg = msg switch {
            Tversion v => NinePMessage.NewMsgTversion(v),
            Tattach v => NinePMessage.NewMsgTattach(v),
            Twalk v => NinePMessage.NewMsgTwalk(v),
            Topen v => NinePMessage.NewMsgTopen(v),
            Tread v => NinePMessage.NewMsgTread(v),
            Twrite v => NinePMessage.NewMsgTwrite(v),
            Tclunk v => NinePMessage.NewMsgTclunk(v),
            Tremove v => NinePMessage.NewMsgTremove(v),
            Tstat v => NinePMessage.NewMsgTstat(v),
            _ => throw new ArgumentException("Unsupported message type")
        };
        return await _dispatcher.DispatchAsync(pmsg, NinePDialect.NineP2000);
    }

    [Fact]
    public async Task Double_Clunk_Returns_Error()
    {
        await Dispatch(new Tattach(1, 100, uint.MaxValue, "user", "mock"));
        
        var result1 = await Dispatch(new Tclunk(2, 100));
        result1.Should().BeOfType<Rclunk>();

        var result2 = await Dispatch(new Tclunk(3, 100));
        result2.Should().BeOfType<Rerror>();
        ((Rerror)result2).Ename.ToLower().Should().Contain("unknown fid");
    }

    [Fact]
    public async Task Use_FID_After_Clunk_Returns_Error()
    {
        await Dispatch(new Tattach(1, 100, uint.MaxValue, "user", "mock"));
        await Dispatch(new Tclunk(2, 100));

        var result = await Dispatch(new Tstat(3, 100));
        result.Should().BeOfType<Rerror>();
    }

    [Fact]
    public async Task Walk_With_Invalid_FID_Returns_Error()
    {
        var result = await Dispatch(new Twalk(1, 9999, 10000, new[] { "etc" }));
        result.Should().BeOfType<Rerror>();
    }

    [Fact]
    public async Task Open_Invalid_FID_Returns_Error()
    {
        var result = await Dispatch(new Topen(1, 9999, 0));
        result.Should().BeOfType<Rerror>();
    }

    [Fact]
    public async Task Read_Invalid_FID_Returns_Error()
    {
        var result = await Dispatch(new Tread(1, 9999, 0, 100));
        result.Should().BeOfType<Rerror>();
    }

    [Fact]
    public async Task Write_Invalid_FID_Returns_Error()
    {
        var result = await Dispatch(new Twrite(1, 9999, 0, new byte[] { 1, 2, 3 }));
        result.Should().BeOfType<Rerror>();
    }

    [Fact]
    public async Task Walk_NewFid_Already_Exists_Returns_Error()
    {
        await Dispatch(new Tattach(1, 100, uint.MaxValue, "user", "mock"));
        await Dispatch(new Twalk(2, 100, 101, new[] { "test" }));

        var result = await Dispatch(new Twalk(3, 100, 101, new[] { "test2" }));
        result.Should().BeOfType<Rerror>();
    }

    [Fact]
    public async Task FID_Lifecycle_Sequence_Validation()
    {
        (await Dispatch(new Tattach(1, 100, uint.MaxValue, "user", "mock"))).Should().BeOfType<Rattach>();
        (await Dispatch(new Twalk(2, 100, 101, new[] { "test.txt" }))).Should().BeOfType<Rwalk>();
        (await Dispatch(new Topen(3, 101, 0))).Should().BeOfType<Ropen>();
        (await Dispatch(new Tread(4, 101, 0, 100))).Should().BeOfType<Rread>();
        (await Dispatch(new Tclunk(5, 101))).Should().BeOfType<Rclunk>();

        var result = await Dispatch(new Tread(6, 101, 0, 100));
        result.Should().BeOfType<Rerror>();
    }

    [Fact]
    public async Task Multiple_Concurrent_FIDs_Tracked_Independently()
    {
        // Create multiple FIDs
        await Dispatch(new Tattach(1, 100, uint.MaxValue, "user", "mock"));
        await Dispatch(new Tattach(2, 200, uint.MaxValue, "user", "mock"));
        await Dispatch(new Tattach(3, 300, uint.MaxValue, "user", "mock"));

        // Clunk FID 200
        var clunkResult = await Dispatch(new Tclunk(4, 200));
        clunkResult.Should().BeOfType<Rclunk>();

        // FID 100 should still work
        var stat100 = await Dispatch(new Tstat(5, 100));
        stat100.Should().BeOfType<Rstat>();

        // FID 300 should still work
        var stat300 = await Dispatch(new Tstat(6, 300));
        stat300.Should().BeOfType<Rstat>();

        // FID 200 should NOT work
        var stat200 = await Dispatch(new Tstat(7, 200));
        stat200.Should().BeOfType<Rerror>();
    }

    [Fact]
    public async Task Remove_Also_Clunks_FID()
    {
        // Attach and walk to a file
        await Dispatch(new Tattach(1, 100, uint.MaxValue, "user", "mock"));
        await Dispatch(new Twalk(2, 100, 101, new[] { "test.txt" }));

        // Remove should also clunk the FID
        await Dispatch(new Tremove(3, 101));

        // Try to use the FID after remove
        var statResult = await Dispatch(new Tstat(4, 101));
        statResult.Should().BeOfType<Rerror>();
    }
}
