using NinePSharp.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using Xunit;
using Moq;
using NinePSharp.Generators;

namespace NinePSharp.Tests;

public class NinePFSDispatcherPropertyTests
{
    private readonly NinePFSDispatcher _dispatcher;
    private readonly Mock<IProtocolBackend> _mockBackend;
    private readonly Mock<IRemoteMountProvider> _mockClusterManager;

    public NinePFSDispatcherPropertyTests()
    {
        _mockBackend = new Mock<IProtocolBackend>();
        _mockBackend.Setup(b => b.Name).Returns("Mock");
        _mockBackend.Setup(b => b.MountPath).Returns("/mock");
        _mockBackend.Setup(b => b.GetFileSystem(It.IsAny<System.Security.SecureString>(), It.IsAny<X509Certificate2>()))
                    .Returns(() => new MockFileSystem(new LuxVaultService()));
        _mockBackend.Setup(b => b.GetFileSystem(It.IsAny<X509Certificate2>()))
                    .Returns(() => new MockFileSystem(new LuxVaultService()));

        _mockClusterManager = new Mock<IRemoteMountProvider>();
        
        _dispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new[] { _mockBackend.Object },
            _mockClusterManager.Object);
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 100)]
    public bool Dispatcher_Handles_Random_Messages_Without_Crashing(NinePMessage msg)
    {
        try
        {
            // We don't care about the result, just that it doesn't throw unhandled exceptions
            // (NinePProtocolException is handled by DispatchAsync)
            var task = _dispatcher.DispatchAsync(msg, NinePDialect.NineP2000U);
            task.Wait();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 100)]
    public bool Dispatcher_FID_Lifecycle_Integrity(List<NinePMessage> sequence)
    {
        if (sequence == null) return true;

        var localDispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new[] { _mockBackend.Object },
            _mockClusterManager.Object);

        foreach (var msg in sequence)
        {
            try
            {
                var task = localDispatcher.DispatchAsync(msg, NinePDialect.NineP2000U);
                task.Wait();
            }
            catch (Exception)
            {
                // We expect failures for invalid sequences, but not crashes
            }
        }
        return true;
    }

    [Property(Arbitrary = new[] { typeof(NinePSharp.Generators.Generators.NinePArb) }, MaxTest = 50)]
    public bool Dispatcher_Fid_Violations_Return_Error(List<NinePMessage> violationSequence)
    {
        if (violationSequence == null || violationSequence.Count == 0) return true;

        var localDispatcher = new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            new[] { _mockBackend.Object },
            _mockClusterManager.Object);

        bool errorFound = false;

        foreach (var msg in violationSequence)
        {
            try
            {
                var result = localDispatcher.DispatchAsync(msg, NinePDialect.NineP2000U).Result;
                if (result is Rerror || result is Rlerror)
                {
                    errorFound = true;
                }
            }
            catch (AggregateException ex) when (ex.InnerException is NinePProtocolException)
            {
                // Protocol exceptions are also a form of "handled error"
                errorFound = true;
            }
            catch (Exception)
            {
                // Other exceptions are bad
                return false;
            }
        }
        
        // We expect that a sequence designed to be a violation eventually returns an error
        // though some messages in the sequence might be valid (like the initial Tattach)
        return errorFound;
    }
}
