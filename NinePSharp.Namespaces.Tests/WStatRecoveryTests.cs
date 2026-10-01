using FsCheck;
using FsCheck.Xunit;
using NinePSharp.Constants;
using Xunit;

namespace NinePSharp.Namespaces.Tests;

public sealed class WStatRecoveryTests
{
    private static readonly ResourceHandle Resource = new(new("recovery", "device", 7), QidType.QTFILE, 3);

    [Fact]
    public void RequestsOwnPayloadAndFingerprintEveryDispatchField()
    {
        byte[] payload = { 1, 2, 3 };
        ResourceOperationContext context = Context(1);
        WStatRecoveryRequest path = WStatRecoveryRequest.ForResource(Resource, payload, context);
        payload[0] = 9;
        Assert.Equal(new byte[] { 1, 2, 3 }, path.Stat.ToArray());
        Assert.Equal(Resource, path.Resource);
        Assert.Null(path.OpenHandle);
        Assert.Equal(context, path.Context);
        Assert.Equal("8DCD61B3D9EBBF9924BB7ADA87531C2BB0CBF3075003E421490FED684B197C76",
            path.Fingerprint);

        var open = new ResourceOpenHandle(Resource, "open", 2, 99, true);
        WStatRecoveryRequest retained = WStatRecoveryRequest.ForOpenHandle(open, path.Stat, context);
        Assert.Equal(open, retained.OpenHandle);
        Assert.Equal("28F6950D2F91E31D060AB002FF3CF8E48F42F4D56E9F1FFDD0BDC8542821DEF4",
            retained.Fingerprint);
        Assert.NotEqual(path.Fingerprint, retained.Fingerprint);
        Assert.NotEqual(path.Fingerprint, WStatRecoveryRequest.ForResource(Resource with { Version = 4 }, path.Stat, context).Fingerprint);
        Assert.NotEqual(path.Fingerprint, WStatRecoveryRequest.ForResource(Resource, new byte[] { 1, 2, 4 }, context).Fingerprint);
        Assert.Equal(path.Fingerprint, WStatRecoveryRequest.ForResource(Resource, path.Stat, Context(2)).Fingerprint);
        Assert.NotEqual(path.Fingerprint, WStatRecoveryRequest.ForResource(Resource, path.Stat,
            new ResourceOperationContext(context.OperationId, 2, "glenda")).Fingerprint);
        Assert.NotEqual(path.Fingerprint, WStatRecoveryRequest.ForResource(Resource, path.Stat,
            new ResourceOperationContext(context.OperationId, 17, "other")).Fingerprint);

        Assert.Throws<ArgumentNullException>(() => WStatRecoveryRequest.ForResource(null!, path.Stat, context));
        Assert.Throws<ArgumentNullException>(() => WStatRecoveryRequest.ForResource(Resource, path.Stat, null!));
        Assert.Throws<ArgumentNullException>(() => WStatRecoveryRequest.ForOpenHandle(null!, path.Stat, context));
    }

    [Property(MaxTest = 100)]
    public void FingerprintChangesWhenAnyPayloadByteChanges(NonEmptyArray<byte> input)
    {
        byte[] first = input.Get;
        byte[] second = first.ToArray();
        int index = first.Length / 2;
        second[index] ^= 0x80;
        Assert.NotEqual(
            WStatRecoveryRequest.ForResource(Resource, first, Context(1)).Fingerprint,
            WStatRecoveryRequest.ForResource(Resource, second, Context(1)).Fingerprint);
    }

    [Fact]
    public async Task MemoryJournalEnforcesIdentityAndTerminalStateTransitions()
    {
        var store = new MemoryWStatRecoveryStore();
        WStatRecoveryRequest first = Request(2, new byte[] { 1 });
        Assert.Equal(WStatRecoveryState.Pending, (await store.BeginAsync(first, default)).State);
        Assert.Same(first, (await store.BeginAsync(first, default)).Request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.BeginAsync(Request(2, new byte[] { 2 }), default).AsTask());
        Assert.Single(await store.GetPendingAsync("session", default));

        await store.CommitAsync(first.Context.OperationId, first.Fingerprint, 7, default);
        await store.CommitAsync(first.Context.OperationId, first.Fingerprint, 7, default);
        WStatRecoveryRecord committed = Assert.IsType<WStatRecoveryRecord>(await store.GetAsync(first.Context.OperationId, default));
        Assert.Equal(WStatRecoveryState.Committed, committed.State);
        Assert.Equal(7U, committed.Result);
        Assert.Null(committed.Error);
        Assert.Empty(await store.GetPendingAsync("session", default));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CommitAsync(first.Context.OperationId, first.Fingerprint, 8, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RejectAsync(first.Context.OperationId, first.Fingerprint, "no", default).AsTask());

        WStatRecoveryRequest rejectedRequest = Request(3, new byte[] { 3 });
        await store.BeginAsync(rejectedRequest, default);
        await store.RejectAsync(rejectedRequest.Context.OperationId, rejectedRequest.Fingerprint, "denied", default);
        await store.RejectAsync(rejectedRequest.Context.OperationId, rejectedRequest.Fingerprint, "denied", default);
        WStatRecoveryRecord rejected = Assert.IsType<WStatRecoveryRecord>(
            await store.GetAsync(rejectedRequest.Context.OperationId, default));
        Assert.Equal(WStatRecoveryState.Rejected, rejected.State);
        Assert.Null(rejected.Result);
        Assert.Equal("denied", rejected.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RejectAsync(
            rejectedRequest.Context.OperationId, rejectedRequest.Fingerprint, "different", default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(
            rejectedRequest.Context.OperationId, rejectedRequest.Fingerprint, 1, default).AsTask());

        ResourceOperationId missing = new("session", 99);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.CommitAsync(missing, "missing", 1, default).AsTask());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RejectAsync(missing, "missing", "no", default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(
            first.Context.OperationId, "wrong", 7, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RejectAsync(
            rejectedRequest.Context.OperationId, "wrong", "denied", default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.GetPendingAsync(" ", default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.RejectAsync(missing, "missing", " ", default).AsTask());
        Assert.Null(await store.GetAsync(missing, default));
    }

    [Fact]
    public async Task MemoryJournalOrdersOnlyPendingOperationsForTheRequestedSession()
    {
        var store = new MemoryWStatRecoveryStore();
        WStatRecoveryRequest third = Request(3, new byte[] { 3 });
        WStatRecoveryRequest first = Request(1, new byte[] { 1 });
        WStatRecoveryRequest second = Request(2, new byte[] { 2 });
        await store.BeginAsync(third, default);
        await store.BeginAsync(first, default);
        await store.BeginAsync(second, default);
        await store.BeginAsync(WStatRecoveryRequest.ForResource(Resource, new byte[] { 9 }, Context(1, "other")), default);
        await store.CommitAsync(second.Context.OperationId, second.Fingerprint, 1, default);
        Assert.Equal(new ulong[] { 1, 3 }, (await store.GetPendingAsync("session", default))
            .Select(record => record.Request.Context.OperationId.Sequence));
        Assert.Single(await store.GetPendingAsync("other", default));
    }

    [Fact]
    public async Task CancellationBeforeJournalAdmissionPreventsDispatch()
    {
        var provider = new ReplayProvider();
        var operations = new DurableFileStatOperations(provider, new MemoryWStatRecoveryStore());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.WStatAsync(
            Resource, new byte[] { 1 }, Context(1), canceled.Token).AsTask());
        Assert.Equal(0, provider.Calls);

        var store = new MemoryWStatRecoveryStore();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.BeginAsync(Request(1), canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetAsync(Context(1).OperationId, canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetPendingAsync("session", canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CommitAsync(
            Context(1).OperationId, "fingerprint", 1, canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RejectAsync(
            Context(1).OperationId, "fingerprint", "denied", canceled.Token).AsTask());
    }

    [Fact]
    public async Task StatOperationsDelegateWithoutEnteringTheJournal()
    {
        var provider = new ReplayProvider { StatResult = new byte[] { 4, 5 } };
        var store = new CountingStore();
        var operations = new DurableFileStatOperations(provider, store);
        using var cancellation = new CancellationTokenSource();
        var open = new ResourceOpenHandle(Resource, "open", 0, 0);
        Assert.Equal(new byte[] { 4, 5 }, (await operations.StatAsync(Resource, 12, cancellation.Token)).ToArray());
        Assert.Equal(new byte[] { 4, 5 }, (await operations.StatAsync(open, 13, cancellation.Token)).ToArray());
        Assert.Equal(new uint[] { 12, 13 }, provider.StatCounts);
        Assert.All(provider.StatTokens, token => Assert.Equal(cancellation.Token, token));
        Assert.Equal(0, store.Begins);
        Assert.Throws<ArgumentNullException>(() => new DurableFileStatOperations(null!, store));
        Assert.Throws<ArgumentNullException>(() => new DurableFileStatOperations(provider, null!));
    }

    [Fact]
    public async Task LostPathReplyRecoversOriginalTargetAndCommitsOnce()
    {
        var provider = new ReplayProvider();
        provider.Outcomes[4] = ProviderOutcome.LoseFirstReply;
        var store = new MemoryWStatRecoveryStore();
        var firstGateway = new DurableFileStatOperations(provider, store);
        byte[] payload = { 4, 5, 6 };
        WStatRecoveryPendingException pending = await Assert.ThrowsAsync<WStatRecoveryPendingException>(() =>
            firstGateway.WStatAsync(Resource, payload, Context(4), default).AsTask());
        Assert.Equal(Context(4), pending.Context);
        payload[0] = 0;
        Assert.Equal(WStatRecoveryState.Pending, (await store.GetAsync(Context(4).OperationId, default))!.State);

        var replacementGateway = new DurableFileStatOperations(provider, store);
        Assert.Equal(3U, await replacementGateway.RecoverAsync(Context(4).OperationId));
        Assert.Equal(1, provider.Applied);
        Assert.Equal(2, provider.Calls);
        Assert.All(provider.MutationTokens, token => Assert.Equal(CancellationToken.None, token));
        Assert.Equal(Resource, provider.Targets[0]);
        Assert.Equal(new byte[] { 4, 5, 6 }, provider.Payloads[1]);
        Assert.Equal(3U, await replacementGateway.RecoverAsync(Context(4).OperationId));
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task LostOpenReplyReplaysTheRetainedHandle()
    {
        var provider = new ReplayProvider();
        provider.Outcomes[5] = ProviderOutcome.LoseFirstReply;
        var store = new MemoryWStatRecoveryStore();
        var operations = new DurableFileStatOperations(provider, store);
        var open = new ResourceOpenHandle(Resource, "retained", 3, 44, true);
        await Assert.ThrowsAsync<WStatRecoveryPendingException>(() =>
            operations.WStatAsync(open, new byte[] { 1, 2 }, Context(5), default).AsTask());
        Assert.Equal(2U, await operations.RecoverAsync(Context(5).OperationId));
        Assert.Equal(new[] { open, open }, provider.OpenTargets);
        Assert.Empty(provider.Targets);
        Assert.Equal(1, provider.Applied);
    }

    [Fact]
    public async Task DefiniteRejectionIsRecordedAndNeverRedispatched()
    {
        var provider = new ReplayProvider();
        provider.Outcomes[6] = ProviderOutcome.Reject;
        var store = new MemoryWStatRecoveryStore();
        var operations = new DurableFileStatOperations(provider, store);
        ResourceWStatRejectedException first = await Assert.ThrowsAsync<ResourceWStatRejectedException>(() =>
            operations.WStatAsync(Resource, new byte[] { 1 }, Context(6), default).AsTask());
        Assert.Equal("provider rejected", first.Message);
        WStatRecoveryRecord record = (await store.GetAsync(Context(6).OperationId, default))!;
        Assert.Equal(WStatRecoveryState.Rejected, record.State);
        Assert.Equal("provider rejected", record.Error);
        ResourceWStatRejectedException replay = await Assert.ThrowsAsync<ResourceWStatRejectedException>(() =>
            operations.RecoverAsync(Context(6).OperationId).AsTask());
        Assert.Equal("provider rejected", replay.Message);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task JournalFailureAfterProviderOutcomeKeepsRecoveryPending()
    {
        var provider = new ReplayProvider();
        var store = new CountingStore { FailNextCommit = true };
        var operations = new DurableFileStatOperations(provider, store);
        await Assert.ThrowsAsync<WStatRecoveryPendingException>(() =>
            operations.WStatAsync(Resource, new byte[] { 1 }, Context(7), default).AsTask());
        Assert.Equal(1U, await operations.RecoverAsync(Context(7).OperationId));
        Assert.Equal(1, provider.Applied);
        Assert.Equal(2, provider.Calls);

        provider.Outcomes[8] = ProviderOutcome.Reject;
        store.FailNextReject = true;
        await Assert.ThrowsAsync<WStatRecoveryPendingException>(() =>
            operations.WStatAsync(Resource, new byte[] { 2 }, Context(8), default).AsTask());
        await Assert.ThrowsAsync<ResourceWStatRejectedException>(() =>
            operations.RecoverAsync(Context(8).OperationId).AsTask());
        Assert.Equal(WStatRecoveryState.Rejected,
            (await store.GetAsync(Context(8).OperationId, default))!.State);
    }

    [Fact]
    public async Task RecoverPendingContinuesPastUnknownOutcomesAndCountsTerminalResolutions()
    {
        var provider = new ReplayProvider();
        provider.Outcomes[2] = ProviderOutcome.Reject;
        provider.Outcomes[3] = ProviderOutcome.Unknown;
        var store = new MemoryWStatRecoveryStore();
        foreach (ulong sequence in new ulong[] { 3, 1, 2 })
            await store.BeginAsync(Request(sequence), default);
        var operations = new DurableFileStatOperations(provider, store);
        Assert.Equal(2, await operations.RecoverPendingAsync("session"));
        Assert.Equal(WStatRecoveryState.Committed, (await store.GetAsync(Context(1).OperationId, default))!.State);
        Assert.Equal(WStatRecoveryState.Rejected, (await store.GetAsync(Context(2).OperationId, default))!.State);
        Assert.Equal(WStatRecoveryState.Pending, (await store.GetAsync(Context(3).OperationId, default))!.State);
        Assert.Equal(new ulong[] { 1, 2, 3 }, provider.Sequences);
    }

    [Fact]
    public async Task RecoverPendingObservesCancellationBetweenJournalReadAndDispatch()
    {
        using var cancellation = new CancellationTokenSource();
        WStatRecoveryRequest request = Request(1);
        var store = new CancelingPendingStore(request, cancellation);
        var provider = new ReplayProvider();
        var operations = new DurableFileStatOperations(provider, store);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            operations.RecoverPendingAsync("session", cancellation.Token).AsTask());
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task AnInnerPendingSignalIsPreservedWithoutNestedWrapping()
    {
        var provider = new ReplayProvider();
        provider.Outcomes[10] = ProviderOutcome.AlreadyPending;
        var operations = new DurableFileStatOperations(provider, new MemoryWStatRecoveryStore());
        WStatRecoveryPendingException thrown = await Assert.ThrowsAsync<WStatRecoveryPendingException>(() =>
            operations.WStatAsync(Resource, new byte[] { 1 }, Context(10), default).AsTask());
        Assert.Same(provider.LastPending, thrown);
    }

    [Fact]
    public async Task MissingAndMalformedTerminalRecordsFailClosed()
    {
        var provider = new ReplayProvider();
        var missing = new DurableFileStatOperations(provider, new MemoryWStatRecoveryStore());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => missing.RecoverAsync(Context(99).OperationId).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => missing.RecoverAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new DurableFileStatOperations(provider,
                new FixedStore(new WStatRecoveryRecord(Request(1), WStatRecoveryState.Committed, 1)))
                .RecoverAsync(null!).AsTask());

        await Assert.ThrowsAsync<InvalidDataException>(() => new DurableFileStatOperations(provider,
            new FixedStore(new WStatRecoveryRecord(Request(1), WStatRecoveryState.Committed)))
            .RecoverAsync(Context(1).OperationId).AsTask());
        ResourceWStatRejectedException rejected = await Assert.ThrowsAsync<ResourceWStatRejectedException>(() =>
            new DurableFileStatOperations(provider,
                new FixedStore(new WStatRecoveryRecord(Request(1), WStatRecoveryState.Rejected)))
                .RecoverAsync(Context(1).OperationId).AsTask());
        Assert.Equal("wstat rejected", rejected.Message);
        ResourceWStatRejectedException admittedRejection = await Assert.ThrowsAsync<ResourceWStatRejectedException>(() =>
            new DurableFileStatOperations(provider,
                new FixedStore(new WStatRecoveryRecord(Request(1), WStatRecoveryState.Rejected)))
                .WStatAsync(Resource, new byte[] { 1 }, Context(1), default).AsTask());
        Assert.Equal("wstat rejected", admittedRejection.Message);
        ResourceWStatRejectedException specificRejection = await Assert.ThrowsAsync<ResourceWStatRejectedException>(() =>
            new DurableFileStatOperations(provider,
                new FixedStore(new WStatRecoveryRecord(Request(1), WStatRecoveryState.Rejected, Error: "specific")))
                .WStatAsync(Resource, new byte[] { 1 }, Context(1), default).AsTask());
        Assert.Equal("specific", specificRejection.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DurableFileStatOperations(provider,
                new FixedStore(new WStatRecoveryRecord(Request(1, new byte[] { 9 }), WStatRecoveryState.Pending)))
                .WStatAsync(Resource, new byte[] { 1 }, Context(1), default).AsTask());
        Assert.Equal(0, provider.Calls);
    }

    private static WStatRecoveryRequest Request(ulong sequence, byte[]? payload = null)
        => WStatRecoveryRequest.ForResource(Resource, payload ?? new byte[] { 1 }, Context(sequence));

    private static ResourceOperationContext Context(ulong sequence, string session = "session")
        => new(new ResourceOperationId(session, sequence), 17, "glenda");

    private enum ProviderOutcome { Success, LoseFirstReply, Reject, Unknown, AlreadyPending }

    private sealed class ReplayProvider : IFileStatOperations
    {
        private readonly Dictionary<ResourceOperationId, uint> completed = new();
        internal Dictionary<ulong, ProviderOutcome> Outcomes { get; } = new();
        internal ReadOnlyMemory<byte> StatResult { get; init; }
        internal List<uint> StatCounts { get; } = new();
        internal List<CancellationToken> StatTokens { get; } = new();
        internal List<CancellationToken> MutationTokens { get; } = new();
        internal List<ResourceHandle> Targets { get; } = new();
        internal List<ResourceOpenHandle> OpenTargets { get; } = new();
        internal List<byte[]> Payloads { get; } = new();
        internal List<ulong> Sequences { get; } = new();
        internal int Calls { get; private set; }
        internal int Applied { get; private set; }
        internal WStatRecoveryPendingException? LastPending { get; private set; }

        public ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceHandle resource, uint count, CancellationToken cancellationToken)
            => Stat(count, cancellationToken);

        public ValueTask<ReadOnlyMemory<byte>> StatAsync(ResourceOpenHandle handle, uint count, CancellationToken cancellationToken)
            => Stat(count, cancellationToken);

        public ValueTask<uint> WStatAsync(ResourceHandle resource, ReadOnlyMemory<byte> stat,
            ResourceOperationContext context, CancellationToken cancellationToken)
        {
            Targets.Add(resource);
            return Mutate(stat, context, cancellationToken);
        }

        public ValueTask<uint> WStatAsync(ResourceOpenHandle handle, ReadOnlyMemory<byte> stat,
            ResourceOperationContext context, CancellationToken cancellationToken)
        {
            OpenTargets.Add(handle);
            return Mutate(stat, context, cancellationToken);
        }

        private ValueTask<ReadOnlyMemory<byte>> Stat(uint count, CancellationToken cancellationToken)
        {
            StatCounts.Add(count);
            StatTokens.Add(cancellationToken);
            return ValueTask.FromResult(StatResult);
        }

        private ValueTask<uint> Mutate(ReadOnlyMemory<byte> stat, ResourceOperationContext context,
            CancellationToken cancellationToken)
        {
            Calls++;
            MutationTokens.Add(cancellationToken);
            Payloads.Add(stat.ToArray());
            Sequences.Add(context.OperationId.Sequence);
            if (completed.TryGetValue(context.OperationId, out uint result))
                return ValueTask.FromResult(result);
            ProviderOutcome outcome = Outcomes.GetValueOrDefault(context.OperationId.Sequence);
            if (outcome == ProviderOutcome.Reject)
                throw new ResourceWStatRejectedException("provider rejected");
            if (outcome == ProviderOutcome.Unknown)
                throw new IOException("provider outcome unknown");
            if (outcome == ProviderOutcome.AlreadyPending)
            {
                LastPending = new WStatRecoveryPendingException(context, new IOException("already pending"));
                throw LastPending;
            }
            result = checked((uint)stat.Length);
            completed.Add(context.OperationId, result);
            Applied++;
            if (outcome == ProviderOutcome.LoseFirstReply)
                throw new IOException("reply lost after commit");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CountingStore : IWStatRecoveryStore
    {
        private readonly MemoryWStatRecoveryStore inner = new();
        internal int Begins { get; private set; }
        internal bool FailNextCommit { get; set; }
        internal bool FailNextReject { get; set; }

        public ValueTask<WStatRecoveryRecord> BeginAsync(WStatRecoveryRequest request, CancellationToken cancellationToken)
        {
            Begins++;
            return inner.BeginAsync(request, cancellationToken);
        }

        public ValueTask<WStatRecoveryRecord?> GetAsync(ResourceOperationId operationId, CancellationToken cancellationToken)
            => inner.GetAsync(operationId, cancellationToken);

        public ValueTask<IReadOnlyList<WStatRecoveryRecord>> GetPendingAsync(string sessionId, CancellationToken cancellationToken)
            => inner.GetPendingAsync(sessionId, cancellationToken);

        public ValueTask CommitAsync(ResourceOperationId operationId, string fingerprint, uint result,
            CancellationToken cancellationToken)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new IOException("journal commit failed");
            }
            return inner.CommitAsync(operationId, fingerprint, result, cancellationToken);
        }

        public ValueTask RejectAsync(ResourceOperationId operationId, string fingerprint, string error,
            CancellationToken cancellationToken)
        {
            if (FailNextReject)
            {
                FailNextReject = false;
                throw new IOException("journal rejection failed");
            }
            return inner.RejectAsync(operationId, fingerprint, error, cancellationToken);
        }
    }

    private sealed class FixedStore(WStatRecoveryRecord record) : IWStatRecoveryStore
    {
        public ValueTask<WStatRecoveryRecord> BeginAsync(WStatRecoveryRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult(record);
        public ValueTask<WStatRecoveryRecord?> GetAsync(ResourceOperationId operationId, CancellationToken cancellationToken)
            => ValueTask.FromResult<WStatRecoveryRecord?>(record);
        public ValueTask<IReadOnlyList<WStatRecoveryRecord>> GetPendingAsync(string sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<WStatRecoveryRecord>>(Array.Empty<WStatRecoveryRecord>());
        public ValueTask CommitAsync(ResourceOperationId operationId, string fingerprint, uint result,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RejectAsync(ResourceOperationId operationId, string fingerprint, string error,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class CancelingPendingStore(
        WStatRecoveryRequest request,
        CancellationTokenSource cancellation) : IWStatRecoveryStore
    {
        public ValueTask<WStatRecoveryRecord> BeginAsync(WStatRecoveryRequest value, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public ValueTask<WStatRecoveryRecord?> GetAsync(ResourceOperationId operationId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<WStatRecoveryRecord>> GetPendingAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return ValueTask.FromResult<IReadOnlyList<WStatRecoveryRecord>>(
                new[] { new WStatRecoveryRecord(request, WStatRecoveryState.Pending) });
        }
        public ValueTask CommitAsync(ResourceOperationId operationId, string fingerprint, uint result,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask RejectAsync(ResourceOperationId operationId, string fingerprint, string error,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
