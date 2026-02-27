using NinePSharp.Constants;
using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Backends;
using NinePSharp.Server.Cluster;
using Moq;
using Xunit;
using FluentAssertions;

namespace NinePSharp.Tests;

public class SecurityAuditTests
{
    [Fact]
    public void ProcessHardening_Verify_Dumpable_Is_Disabled()
    {
        // This test verifies that PR_SET_DUMPABLE was actually applied.
        if (OperatingSystem.IsLinux())
        {
            // PR_GET_DUMPABLE = 3
            int result = ReflectionHelper.InvokeStatic<int>(typeof(NinePSharp.Server.Utils.NativeMethods), "prctl", 3, (nuint)0, (nuint)0, (nuint)0, (nuint)0);
            result.Should().Be(0, "Process should not be dumpable (PR_SET_DUMPABLE=0 was applied)");
        }
    }

    private static class ReflectionHelper
    {
        public static T InvokeStatic<T>(Type type, string methodName, params object[] args)
        {
            var method = type.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null) throw new Exception($"Method {methodName} not found on {type.Name}");
            return (T)method.Invoke(null, args)!;
        }
    }

    [Fact]
    public async Task Dispatcher_Handles_Fid_Flooding_Stress()
    {
        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance, 
            Enumerable.Empty<IProtocolBackend>(), 
            new Mock<IClusterManager>().Object);

        const int floodCount = 100_000;
        Console.WriteLine($"[Audit] Flooding dispatcher with {floodCount} Tattach requests...");

        // Create 100k FIDs without clunking
        for (uint i = 0; i < floodCount; i++)
        {
            var attach = new Tattach(1, i, NinePConstants.NoFid, "user", "");
            await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTattach(attach), NinePDialect.NineP2000);
        }

        // Verify we can still use the last one
        var stat = new Tstat(1, floodCount - 1);
        var response = await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTstat(stat), NinePDialect.NineP2000);
        response.Should().BeOfType<Rstat>();

        Console.WriteLine("[Audit] Dispatcher maintained stability under 100k FID load.");
    }

    [Fact]
    public async Task AuthFid_Flooding_Exhaustion_Test()
    {
        var dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance, 
            Enumerable.Empty<IProtocolBackend>(), 
            new Mock<IClusterManager>().Object);

        const int floodCount = 50_000;
        
        // AuthFids create a SecureString in memory
        for (uint i = 0; i < floodCount; i++)
        {
            var tauth = new Tauth(1, i, "user", "");
            await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTauth(tauth), NinePDialect.NineP2000);
        }

        // Cleanup to prevent actual OOM in the test runner
        for (uint i = 0; i < floodCount; i++)
        {
            var tclunk = new Tclunk(1, i);
            await dispatcher.DispatchAsync(NinePSharp.Parser.NinePMessage.NewMsgTclunk(tclunk), NinePDialect.NineP2000);
        }
    }
}
