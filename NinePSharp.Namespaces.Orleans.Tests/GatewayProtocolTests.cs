using FsCheck.Xunit;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Namespaces.Orleans.Server;
using NinePSharp.Namespaces.Orleans.Tests.Support;
using NinePSharp.Parser;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests;

public sealed class GatewayProtocolTests
{
    [Fact]
    public async Task LinuxFidErrorsDistinguishDuplicateDirectoryAndInvalidOpenState()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();
        var duplicate = await test.SendAsync(NinePMessage.NewMsgTattach(new Tattach(40, 1, NinePConstants.NoFid, "user", "/")), dialect: NinePDialect.NineP2000L);
        Assert.Equal((uint)LinuxErrorCode.EEXIST, Assert.IsType<Rlerror>(duplicate).Ecode);
        var directory = await test.SendAsync(NinePMessage.NewMsgTopen(new Topen(41, 1, NinePConstants.ORDWR)), dialect: NinePDialect.NineP2000L);
        Assert.Equal((uint)LinuxErrorCode.EISDIR, Assert.IsType<Rlerror>(directory).Ecode);
        await test.SendAsync(NinePMessage.NewMsgTwalk(new Twalk(42, 1, 3, new[] { "file" })));
        var unopened = await test.SendAsync(NinePMessage.NewMsgTread(new Tread(43, 3, 0, 1)), dialect: NinePDialect.NineP2000L);
        Assert.Equal((uint)LinuxErrorCode.EINVAL, Assert.IsType<Rlerror>(unopened).Ecode);
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Fact]
    public async Task FlushWaitsForOldRequestCompletionAfterRequestingCancellation()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Resources.Setup(value => value.ReadAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ulong>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
            .Returns(async (ResourceOpenHandle handle, ulong offset, uint count, CancellationToken token) =>
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                started.TrySetResult();
                await release.Task;
                return ReadOnlyMemory<byte>.Empty;
            });
        Task<object> read = test.SendAsync(NinePMessage.NewMsgTread(new Tread(50, 2, 0, 1)));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<object> flush = test.SendAsync(NinePMessage.NewMsgTflush(new Tflush(51, 50)));
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(flush.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await read.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsType<Rflush>(await flush.WaitAsync(TimeSpan.FromSeconds(2)));
            await test.Dispatcher.CloseSessionAsync("unit");
        }
    }

    [Fact]
    public async Task ConcurrentRequestsCannotReuseAnInFlightTag()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Resources.Setup(value => value.ReadAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ulong>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
            .Returns(async (ResourceOpenHandle handle, ulong offset, uint count, CancellationToken token) =>
            {
                started.TrySetResult();
                await release.Task;
                return ReadOnlyMemory<byte>.Empty;
            });
        Task<object> first = test.SendAsync(NinePMessage.NewMsgTread(new Tread(50, 2, 0, 1)));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        try
        {
            var duplicate = Assert.IsType<Rerror>(await test.SendAsync(
                NinePMessage.NewMsgTstat(new Tstat(50, 2))));
            Assert.Equal((ushort)50, duplicate.Tag);
            Assert.Contains("duplicate", duplicate.Ename);
        }
        finally
        {
            release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(1));
            await test.Dispatcher.CloseSessionAsync("unit").WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task CloseWaitsForInitializingAttachAndCancelsIt()
    {
        var test = new GatewayTestContext();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var policy = new Mock<IDistributedNamespaceAttachResolver>();
        policy.Setup(value => value.ResolveAsync(It.IsAny<string>(), It.IsAny<Tattach>(), It.IsAny<NinePDialect>(), null, It.IsAny<CancellationToken>()))
            .Returns(async (string id, Tattach request, NinePDialect dialect, System.Security.Cryptography.X509Certificates.X509Certificate2? certificate, CancellationToken token) =>
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                started.TrySetResult();
                await release.Task;
                return new DistributedNamespaceAttach("group", 1, "user", GatewayTestContext.Root);
            });
        var dispatcher = new DistributedNamespaceDispatcher(new DistributedNamespaceOperations(test.Factory.Object, test.Resources.Object), policy.Object);
        Task<object> attach = dispatcher.DispatchAsync("close", NinePMessage.NewMsgTattach(new Tattach(1, 1, NinePConstants.NoFid, "user", "/")), NinePDialect.NineP2000);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task closing = dispatcher.CloseSessionAsync("close");
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(closing.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await attach.WaitAsync(TimeSpan.FromSeconds(2));
            await closing.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(1U)]
    [InlineData(245U)]
    [InlineData(uint.MaxValue)]
    public async Task ReadSizeBoundaryIsExact(uint requested)
    {
        var test = new GatewayTestContext();
        await test.SendAsync(NinePMessage.NewMsgTversion(new Tversion(65535, 256, "9P2000")));
        await test.OpenFileAsync();
        var read = Assert.IsType<Rread>(await test.SendAsync(NinePMessage.NewMsgTread(new Tread(123, 2, 11, requested))));
        Assert.Equal((ushort)123, read.Tag);
        Assert.Equal(Math.Min(requested, 245U), read.Count);
        Assert.Equal(11U + read.Count, read.Size);
        test.Resources.Verify(value => value.ReadAsync(It.IsAny<ResourceOpenHandle>(), 11, Math.Min(requested, 245U), It.IsAny<CancellationToken>()), Times.Once);
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Fact]
    public async Task ErrorsCarryDialectSpecificCodesAndOriginalTags()
    {
        var test = new GatewayTestContext();
        var missing = NinePMessage.NewMsgTstat(new Tstat(123, 99));
        Assert.Equal((uint)LinuxErrorCode.EBADF, Assert.IsType<Rlerror>(await test.SendAsync(missing, dialect: NinePDialect.NineP2000L)).Ecode);
        await test.OpenFileAsync();
        var request = NinePMessage.NewMsgTstat(new Tstat(123, 2));
        test.Resources.Setup(value => value.StatAsync(It.IsAny<ResourceHandle>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        Assert.Equal((uint)LinuxErrorCode.ECANCELED, Assert.IsType<Rlerror>(await test.SendAsync(request, dialect: NinePDialect.NineP2000L)).Ecode);
        var classic = Assert.IsType<Rerror>(await test.SendAsync(request));
        Assert.Null(classic.Ecode);
        Assert.Equal("interrupted", classic.Ename);
        Assert.Equal((ushort)123, classic.Tag);
        var unix = Assert.IsType<Rerror>(await test.SendAsync(request, dialect: NinePDialect.NineP2000U));
        Assert.Equal((uint)LinuxErrorCode.ECANCELED, unix.Ecode);
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Fact]
    public async Task DirectoryRepliesContainWholeStatRecordsAtExactBoundaries()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();
        await test.SendAsync(NinePMessage.NewMsgTopen(new Topen(4, 1, NinePConstants.OREAD)));
        test.Resources.Setup(value => value.ReadDirectoryAsync(It.IsAny<ResourceHandle>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ResourceDirectoryEntry("one", GatewayTestContext.File), new ResourceDirectoryEntry("two", GatewayTestContext.File) });
        uint entrySize = Stat.CalculateSize("one", "user", "user", "user", NinePDialect.NineP2000);
        var shortRead = Assert.IsType<Rread>(await test.SendAsync(NinePMessage.NewMsgTread(new Tread(5, 1, 0, entrySize - 1))));
        Assert.Empty(shortRead.Data.ToArray());
        var first = Assert.IsType<Rread>(await test.SendAsync(NinePMessage.NewMsgTread(new Tread(6, 1, 0, entrySize))));
        Assert.Equal(entrySize, first.Count);
        int offset = 0;
        Assert.Equal("one", new Stat(first.Data.Span, ref offset).Name);
        Assert.Equal((int)entrySize, offset);
        var second = Assert.IsType<Rread>(await test.SendAsync(NinePMessage.NewMsgTread(new Tread(7, 1, entrySize, entrySize))));
        offset = 0;
        Assert.Equal("two", new Stat(second.Data.Span, ref offset).Name);
        var both = Assert.IsType<Rread>(await test.SendAsync(NinePMessage.NewMsgTread(new Tread(8, 1, 0, entrySize * 2))));
        Assert.Equal(first.Data.ToArray().Concat(second.Data.ToArray()), both.Data.ToArray());
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Fact]
    public async Task LinuxDirectoryReplyHasExactSizeAndHonorsCount()
    {
        var test = new GatewayTestContext();
        await test.SendAsync(NinePMessage.NewMsgTversion(new Tversion(65535, 256, "9P2000.L")));
        await test.OpenFileAsync();
        await test.SendAsync(NinePMessage.NewMsgTopen(new Topen(4, 1, NinePConstants.OREAD)));
        var empty = Assert.IsType<Rreaddir>(await test.SendAsync(NinePMessage.NewMsgTreaddir(new Treaddir(23, 5, 1, 0, 0))));
        Assert.Equal(11U, empty.Size);
        Assert.Equal(0U, empty.Count);
        var response = Assert.IsType<Rreaddir>(await test.SendAsync(NinePMessage.NewMsgTreaddir(new Treaddir(23, 6, 1, 0, uint.MaxValue))));
        Assert.Equal((uint)response.Data.Length, response.Count);
        Assert.Equal(response.Count + 11, response.Size);
        Assert.NotEmpty(response.Data.ToArray());
        byte[] encoded = new byte[response.Size];
        response.WriteTo(encoded);
        Assert.Equal(response.Data.ToArray(), new Rreaddir(encoded).Data.ToArray());
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Fact]
    public async Task LinuxCreatePassesOnlyPermissionBitsToProvider()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();
        test.Resources.Setup(value => value.CreateAndOpenAsync(
            It.IsAny<ResourceHandle>(),
            "new",
            0x1A4,
            NinePConstants.ORDWR,
            It.IsAny<ResourceOperationContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceOpenHandle(GatewayTestContext.File, "new", NinePConstants.ORDWR, 99));
        var result = Assert.IsType<Rlcreate>(await test.SendAsync(
            NinePMessage.NewMsgTlcreate(new Tlcreate(28, 7, 1, "new", 2, 0x81A4, 0))));
        Assert.Equal(99U, result.Iounit);
        test.Resources.Verify(
            value => value.CreateAndOpenAsync(
                It.IsAny<ResourceHandle>(),
                "new",
                0x1A4,
                NinePConstants.ORDWR,
                It.IsAny<ResourceOperationContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Fact]
    public async Task ClassicCreateReturnsTheCreatedResourceAndIoUnit()
    {
        var test = new GatewayTestContext();
        await test.SendAsync(NinePMessage.NewMsgTattach(
            new Tattach(1, 1, NinePConstants.NoFid, "user", "/")));
        test.Resources.Setup(value => value.CreateAndOpenAsync(
                It.IsAny<ResourceHandle>(),
                "new",
                NinePConstants.Mode0600,
                NinePConstants.ORDWR,
                It.IsAny<ResourceOperationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceOpenHandle(GatewayTestContext.File, "new", NinePConstants.ORDWR, 99));

        var created = Assert.IsType<Rcreate>(await test.SendAsync(NinePMessage.NewMsgTcreate(
            new Tcreate(2, 1, "new", NinePConstants.Mode0600, NinePConstants.ORDWR))));

        Assert.Equal((ushort)2, created.Tag);
        Assert.Equal(GatewayTestContext.File.Qid, created.Qid);
        Assert.Equal(99U, created.Iounit);
        await test.Dispatcher.CloseSessionAsync("unit").WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task InvalidDependenciesAndSessionArgumentsAreRejected()
    {
        var test = new GatewayTestContext();
        var operations = new DistributedNamespaceOperations(test.Factory.Object, test.Resources.Object);
        Assert.Throws<ArgumentNullException>(() => new DistributedNamespaceDispatcher(null!, test.Attach));
        Assert.Throws<ArgumentNullException>(() => new DistributedNamespaceDispatcher(operations, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GatewayTestContext(255));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GatewayTestContext((uint)int.MaxValue + 1));
        _ = new GatewayTestContext(256);
        _ = new GatewayTestContext(int.MaxValue);
        await Assert.ThrowsAsync<ArgumentNullException>(() => test.Dispatcher.DispatchAsync("unit", null!, NinePDialect.NineP2000));
        await Assert.ThrowsAsync<ArgumentException>(() => test.SendAsync(NinePMessage.NewMsgTflush(new Tflush(1, 2)), " "));
        await test.Dispatcher.CloseSessionAsync("unknown");
    }

    [Theory]
    [InlineData(255U, false)]
    [InlineData(256U, true)]
    [InlineData(uint.MaxValue, true)]
    public async Task ExactNegotiationBoundaries(uint requested, bool success)
    {
        var test = new GatewayTestContext(256);
        object response = await test.SendAsync(NinePMessage.NewMsgTversion(new Tversion(65535, requested, "9P2000")));
        if (success)
        {
            Assert.Equal(256U, Assert.IsType<Rversion>(response).MSize);
        }
        else
        {
            Assert.IsType<Rerror>(response);
        }

        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Theory]
    [InlineData("process")]
    [InlineData("user")]
    public async Task AdditionalAttachesCannotChangeSecurityContext(string field)
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();
        if (field == "process")
        {
            test.Attach.ProcessId = 2;
        }
        else
        {
            test.Attach.User = "other";
        }

        Assert.IsType<Rerror>(await test.SendAsync(NinePMessage.NewMsgTattach(new Tattach(4, 3, NinePConstants.NoFid, "user", "/"))));
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    public async Task ErrorTextIsBoundedInUtf8Bytes(int bytes)
    {
        var test = new GatewayTestContext();
        await test.SendAsync(NinePMessage.NewMsgTversion(new Tversion(65535, 256, "9P2000.u")));
        await test.OpenFileAsync();
        string message = new('x', bytes);
        test.Resources.Setup(value => value.StatAsync(It.IsAny<ResourceHandle>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException(message));
        var response = Assert.IsType<Rerror>(await test.SendAsync(NinePMessage.NewMsgTstat(new Tstat(4, 2)), dialect: NinePDialect.NineP2000U));
        Assert.Equal(bytes == 200 ? message : "resource operation failed", response.Ename);
        Assert.Equal((uint)LinuxErrorCode.EIO, response.Ecode);
        Assert.True(response.Size <= 256);
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Theory]
    [InlineData("9P2000", "9P2000")]
    [InlineData("9P2000.u", "9P2000.u")]
    [InlineData("9P2000.L", "9P2000.L")]
    [InlineData("9P2000.future", "9P2000")]
    [InlineData("invalid", "unknown")]
    public async Task NegotiationHonorsSupportedVersions(string requested, string expected)
    {
        var test = new GatewayTestContext();
        var result = Assert.IsType<Rversion>(await test.SendAsync(NinePMessage.NewMsgTversion(new Tversion(65535, 8192, requested))));
        Assert.Equal(expected, result.Version);
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Property(MaxTest = 100)]
    public bool NegotiatedSizeNeverExceedsEitherPeer(uint requested, ushort serverDelta)
    {
        uint maximum = 256U + serverDelta;
        var test = new GatewayTestContext(maximum);
        object response = test.SendAsync(NinePMessage.NewMsgTversion(new Tversion(65535, requested, "9P2000"))).GetAwaiter().GetResult();
        test.Dispatcher.CloseSessionAsync("unit").GetAwaiter().GetResult();
        return requested < 256 ? response is Rerror : response is Rversion version && version.MSize == Math.Min(requested, maximum);
    }

    [Property(MaxTest = 100)]
    public bool ReadReplyFitsNegotiatedSizeAndRequestedCount(ushort sizeDelta, uint requested)
    {
        var test = new GatewayTestContext();
        uint msize = 256U + sizeDelta;
        test.SendAsync(NinePMessage.NewMsgTversion(new Tversion(65535, msize, "9P2000"))).GetAwaiter().GetResult();
        test.OpenFileAsync().GetAwaiter().GetResult();
        var result = Assert.IsType<Rread>(test.SendAsync(NinePMessage.NewMsgTread(new Tread(4, 2, 0, requested))).GetAwaiter().GetResult());
        test.Dispatcher.CloseSessionAsync("unit").GetAwaiter().GetResult();
        return result.Data.Length == Math.Min(requested, msize - 11) && result.Size <= msize;
    }

    [Fact]
    public async Task MissingFirstWalkElementReturnsErrorAndDoesNotAllocateFid()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();
        Assert.IsType<Rerror>(await test.SendAsync(NinePMessage.NewMsgTwalk(new Twalk(4, 1, 3, new[] { "missing" }))));
        Assert.IsType<Rerror>(await test.SendAsync(NinePMessage.NewMsgTstat(new Tstat(5, 3))));
        Assert.IsType<Rwalk>(await test.SendAsync(NinePMessage.NewMsgTwalk(new Twalk(6, 1, 3, Array.Empty<string>()))));
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Fact]
    public async Task VersionResetChangesOperationEpochAndClunksOldFids()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();
        await test.SendAsync(NinePMessage.NewMsgTversion(new Tversion(65535, 8192, "9P2000")));
        Assert.IsType<Rerror>(await test.SendAsync(NinePMessage.NewMsgTstat(new Tstat(7, 2))));
        await test.OpenFileAsync();
        Assert.Equal(2, test.Mutations.Count);
        Assert.NotEqual(test.Mutations[0].OperationId, test.Mutations[1].OperationId);
        test.Resources.Verify(value => value.ClunkAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ResourceOperationContext>(), It.IsAny<CancellationToken>()), Times.Once);
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Fact]
    public async Task OversizedProviderResponseBecomesProtocolError()
    {
        var test = new GatewayTestContext();
        await test.OpenFileAsync();
        test.Resources.Setup(value => value.ReadAsync(It.IsAny<ResourceOpenHandle>(), It.IsAny<ulong>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<byte>)new byte[9000]);
        var error = Assert.IsType<Rerror>(await test.SendAsync(NinePMessage.NewMsgTread(new Tread(4, 2, 0, 9000))));
        Assert.Contains("msize", error.Ename);
        await test.Dispatcher.CloseSessionAsync("unit");
    }

    [Fact]
    public async Task AttachPolicyAndNamespaceIsolationAreEnforced()
    {
        var test = new GatewayTestContext();
        test.Attach.Deny = true;
        var request = NinePMessage.NewMsgTattach(new Tattach(1, 1, NinePConstants.NoFid, "user", "/"));
        var denied = Assert.IsType<Rlerror>(await test.SendAsync(request, dialect: NinePDialect.NineP2000L));
        Assert.Equal((uint)LinuxErrorCode.EACCESS, denied.Ecode);
        test.Attach.Deny = false;
        Assert.IsType<Rattach>(await test.SendAsync(request));
        test.Attach.Group = "another-group";
        Assert.IsType<Rerror>(await test.SendAsync(NinePMessage.NewMsgTattach(new Tattach(2, 2, NinePConstants.NoFid, "user", "/"))));
        await test.Dispatcher.CloseSessionAsync("unit");
    }
}
