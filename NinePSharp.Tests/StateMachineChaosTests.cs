using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Configuration.Models;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;

namespace NinePSharp.Tests;

public class StateMachineChaosTests
{
    private readonly IClusterManager _clusterManager;
    private readonly Mock<IProtocolBackend> _mockBackend;
    private readonly Mock<INinePFileSystem> _mockFs;

    public StateMachineChaosTests()
    {
        _clusterManager = new Mock<IClusterManager>().Object;
        _mockBackend = new Mock<IProtocolBackend>();
        _mockFs = new Mock<INinePFileSystem>();
        
        _mockBackend.Setup(b => b.MountPath).Returns("/chaos");
        _mockBackend.Setup(b => b.GetFileSystem(It.IsAny<SecureString>(), It.IsAny<X509Certificate2>())).Returns(_mockFs.Object);
        _mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>())).Returns(_mockFs.Object);
        _mockFs.Setup(f => f.Clone()).Returns(_mockFs.Object);
    }

    [Fact]
    public async Task Read_Without_Attach_Returns_Error()
    {
        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { _mockBackend.Object }, _clusterManager);
        
        // Try reading FID 100 which was never attached/walked
        var tread = new Tread(1, 100, 0, 1024);
        var response = await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTread(tread), NinePDialect.NineP2000);

        response.Should().BeOfType<Rerror>();
        ((Rerror)response).Ename.Should().Contain("Unknown FID");
    }

    [Fact]
    public async Task Double_Clunk_Returns_Error()
    {
        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { _mockBackend.Object }, _clusterManager);
        
        // 1. Attach
        await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTattach(new Tattach(1, 1, uint.MaxValue, "scott", "chaos")), NinePDialect.NineP2000);

        // 2. First Clunk
        var tclunk = new Tclunk(1, 1);
        _mockFs.Setup(f => f.ClunkAsync(tclunk)).ReturnsAsync(new Rclunk(1));
        await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTclunk(tclunk), NinePDialect.NineP2000);

        // 3. Second Clunk (should fail because FID 1 is gone)
        var response = await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTclunk(tclunk), NinePDialect.NineP2000);

        response.Should().BeOfType<Rerror>();
        ((Rerror)response).Ename.Should().Contain("Unknown FID");
    }

    [Fact]
    public async Task Massive_FID_Usage_Does_Not_Crash()
    {
        var dispatcher = new NinePFSDispatcher(NullLogger<NinePFSDispatcher>.Instance, new[] { _mockBackend.Object }, _clusterManager);
        
        // Open 5000 FIDs simultaneously
        for (uint i = 0; i < 5000; i++)
        {
            await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTattach(new Tattach(1, i, uint.MaxValue, "scott", "chaos")), NinePDialect.NineP2000);
        }

        // Verify some random FID exists
        var tstat = new Tstat(1, 2500);
        _mockFs.Setup(f => f.StatAsync(tstat)).ReturnsAsync(new Rstat(1, new Stat()));
        var response = await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTstat(tstat), NinePDialect.NineP2000);
        
        response.Should().BeOfType<Rstat>();
    }
}
