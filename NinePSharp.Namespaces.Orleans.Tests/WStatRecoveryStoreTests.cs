using Moq;
using NinePSharp.Constants;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace NinePSharp.Namespaces.Orleans.Tests;

public sealed class WStatRecoveryStoreTests
{
    private static readonly ResourceHandle Resource = new(new("provider", "device", 7), QidType.QTFILE, 3);

    [Fact]
    public void RecoveryModelsRoundTripPathAndOpenRequestsAndRejectCorruption()
    {
        WStatRecoveryRequest path = Request(1);
        WStatRecoveryRequestModel pathModel = path.ToModel();
        Assert.Equal(path.Fingerprint, pathModel.ToDomain().Fingerprint);
        Assert.Equal(path.Stat.ToArray(), pathModel.Stat);
        pathModel.Stat[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => pathModel.ToDomain());

        var open = new ResourceOpenHandle(Resource, "retained", 3, 44, true);
        WStatRecoveryRequest retained = WStatRecoveryRequest.ForOpenHandle(open, new byte[] { 4, 5 }, Context(2));
        WStatRecoveryRequestModel openModel = retained.ToModel();
        Assert.Equal(open, openModel.ToDomain().OpenHandle);
        Assert.Throws<InvalidDataException>(() => (openModel with
        {
            Resource = (Resource with { Version = 4 }).ToModel(),
        }).ToDomain());

        var committed = new WStatRecoveryRecord(retained, WStatRecoveryState.Committed, 2);
        WStatRecoveryRecord roundTrip = committed.ToModel().ToDomain();
        Assert.Equal(committed.State, roundTrip.State);
        Assert.Equal(committed.Result, roundTrip.Result);
        Assert.Equal(committed.Request.Fingerprint, roundTrip.Request.Fingerprint);
        Assert.Throws<InvalidDataException>(() => (committed.ToModel() with
        {
            State = (WStatRecoveryStateModel)99,
        }).ToDomain());
    }

    [Fact]
    public async Task OrleansStoreMapsEveryOperationToItsSessionGrain()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var grain = new Mock<IWStatRecoveryJournalGrain>(MockBehavior.Strict);
        factory.Setup(value => value.GetGrain<IWStatRecoveryJournalGrain>("session", null)).Returns(grain.Object);
        WStatRecoveryRequest request = Request(3);
        WStatRecoveryRecordModel pending = new(request.ToModel(), WStatRecoveryStateModel.Pending, null, null);
        WStatRecoveryRecordModel committed = pending with { State = WStatRecoveryStateModel.Committed, Result = 3 };
        grain.Setup(value => value.BeginAsync(It.Is<WStatRecoveryRequestModel>(model =>
            model.Fingerprint == request.Fingerprint))).ReturnsAsync(pending);
        grain.Setup(value => value.GetAsync(3)).ReturnsAsync(committed);
        grain.Setup(value => value.GetPendingAsync()).ReturnsAsync(new[] { pending });
        grain.Setup(value => value.CommitAsync(3, request.Fingerprint, 3)).Returns(Task.CompletedTask);
        grain.Setup(value => value.RejectAsync(3, request.Fingerprint, "denied")).Returns(Task.CompletedTask);
        var store = new OrleansWStatRecoveryStore(factory.Object);

        Assert.Equal(WStatRecoveryState.Pending, (await store.BeginAsync(request, default)).State);
        Assert.Equal(3U, (await store.GetAsync(request.Context.OperationId, default))!.Result);
        Assert.Single(await store.GetPendingAsync("session", default));
        await store.CommitAsync(request.Context.OperationId, request.Fingerprint, 3, default);
        await store.RejectAsync(request.Context.OperationId, request.Fingerprint, "denied", default);
        grain.VerifyAll();
        factory.Verify(value => value.GetGrain<IWStatRecoveryJournalGrain>("session", null), Times.Exactly(5));
        Assert.Throws<ArgumentNullException>(() => new OrleansWStatRecoveryStore(null!));
    }

    [Fact]
    public async Task OrleansStoreValidatesInputsAndCancellationBeforeResolvingAGrain()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var store = new OrleansWStatRecoveryStore(factory.Object);
        var canceled = new CancellationToken(true);
        ResourceOperationId id = Context(1).OperationId;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.BeginAsync(Request(1), canceled).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.GetAsync(null!, default).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetAsync(id, canceled).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.GetPendingAsync(" ", default).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetPendingAsync("session", canceled).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.CommitAsync(null!, "x", 1, default).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CommitAsync(id, "x", 1, canceled).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.RejectAsync(null!, "x", "no", default).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RejectAsync(id, "x", "no", canceled).AsTask());
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OrleansStoreHonorsCancellationWhileWaitingForTheGrain()
    {
        var factory = new Mock<IGrainFactory>(MockBehavior.Strict);
        var grain = new Mock<IWStatRecoveryJournalGrain>(MockBehavior.Strict);
        factory.Setup(value => value.GetGrain<IWStatRecoveryJournalGrain>("session", null)).Returns(grain.Object);
        var completion = new TaskCompletionSource<WStatRecoveryRecordModel>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        grain.Setup(value => value.BeginAsync(It.IsAny<WStatRecoveryRequestModel>())).Returns(completion.Task);
        var store = new OrleansWStatRecoveryStore(factory.Object);
        using var cancellation = new CancellationTokenSource();
        Task<WStatRecoveryRecord> waiting = store.BeginAsync(Request(1), cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(completion.Task.IsCompleted);
    }

    [Fact]
    public async Task JournalGrainWritesEveryNewStateAndOrdersPendingEntries()
    {
        var persistent = new Mock<IPersistentState<WStatRecoveryPersistentState>>(MockBehavior.Strict);
        var state = new WStatRecoveryPersistentState();
        persistent.SetupGet(value => value.State).Returns(state);
        persistent.Setup(value => value.WriteStateAsync())
            .Returns(Task.CompletedTask);
        var grain = new WStatRecoveryJournalGrain(persistent.Object, "session");
        Assert.Throws<ArgumentException>(() => new WStatRecoveryJournalGrain(persistent.Object, " "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => grain.BeginAsync(null!));
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.BeginAsync(
            WStatRecoveryRequest.ForResource(
                Resource,
                new byte[] { 1 },
                new ResourceOperationContext(new("other", 1), 1, "glenda")).ToModel()));

        WStatRecoveryRequest first = Request(1);
        WStatRecoveryRequest second = Request(2);
        WStatRecoveryRequest third = Request(3);
        await grain.BeginAsync(third.ToModel());
        await grain.BeginAsync(first.ToModel());
        await grain.BeginAsync(second.ToModel());
        await grain.BeginAsync(first.ToModel());
        Assert.Equal(new ulong[] { 1, 2, 3 }, (await grain.GetPendingAsync())
            .Select(record => record.Request.Context.OperationId.Sequence));
        persistent.Verify(value => value.WriteStateAsync(), Times.Exactly(3));

        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.CommitAsync(1, "wrong", 3));
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.RejectAsync(2, "wrong", "denied"));
        await grain.CommitAsync(1, first.Fingerprint, 3);
        await grain.CommitAsync(1, first.Fingerprint, 3);
        await grain.RejectAsync(2, second.Fingerprint, "denied");
        await grain.RejectAsync(2, second.Fingerprint, "denied");
        persistent.Verify(value => value.WriteStateAsync(), Times.Exactly(5));
        Assert.Equal(new ulong[] { 3 }, (await grain.GetPendingAsync())
            .Select(record => record.Request.Context.OperationId.Sequence));
        persistent.VerifyAll();
    }

    private static WStatRecoveryRequest Request(ulong sequence)
        => WStatRecoveryRequest.ForResource(Resource, new byte[] { 1, 2, 3 }, Context(sequence));

    private static ResourceOperationContext Context(ulong sequence)
        => new(new ResourceOperationId("session", sequence), 17, "glenda");
}
