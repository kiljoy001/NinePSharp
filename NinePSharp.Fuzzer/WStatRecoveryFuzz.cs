using NinePSharp.Constants;
using NinePSharp.Namespaces;

namespace NinePSharp.Fuzzer;

internal static class WStatRecoveryFuzz
{
    private enum RecoveryOutcome
    {
        Success,
        LostReply,
        Rejected,
        Unknown,
    }

    internal static void Run(byte[] data) => RunAsync(data).GetAwaiter().GetResult();

    private static async Task RunAsync(byte[] data)
    {
        var resource = new ResourceHandle(new("wstat-fuzz", "device", 1), QidType.QTFILE);
        var open = new ResourceOpenHandle(resource, "retained", 2, 0);
        var provider = new RecoveryProvider();
        var store = new MemoryWStatRecoveryStore();
        var operations = new DurableFileStatOperations(provider, store);
        int count = Math.Min(data.Length, 64);
        for (int index = 0; index < count; index++)
        {
            ulong sequence = checked((ulong)index + 1);
            var context = new ResourceOperationContext(new("wstat-fuzz", sequence), 1, "fuzzer");
            byte[] payload = { data[index], (byte)index };
            provider.Outcomes[sequence] = (RecoveryOutcome)(data[index] & 3);
            try
            {
                if ((data[index] & 4) == 0)
                {
                    await operations.WStatAsync(resource, payload, context, default);
                }
                else
                {
                    await operations.WStatAsync(open, payload, context, default);
                }
            }
            catch (ResourceWStatRejectedException)
            {
            }
            catch (WStatRecoveryPendingException)
            {
            }

            WStatRecoveryRecord admitted = (await store.GetAsync(context.OperationId, default))
                ?? throw new InvalidOperationException("wstat was dispatched without durable admission");
            try
            {
                await operations.WStatAsync(resource, new byte[] { (byte)(data[index] ^ 0x80) }, context, default);
                throw new InvalidOperationException("operation identity collision was accepted");
            }
            catch (InvalidOperationException collision) when (
                collision.Message.Contains("different wstat request", StringComparison.Ordinal))
            {
            }

            if (admitted.State == WStatRecoveryState.Pending)
            {
                provider.AllowRecovery(sequence);
                try
                {
                    await operations.RecoverAsync(context.OperationId);
                }
                catch (ResourceWStatRejectedException)
                {
                }
                catch (WStatRecoveryPendingException)
                {
                }
            }

            WStatRecoveryRecord final = (await store.GetAsync(context.OperationId, default))!;
            if (provider.Applied.GetValueOrDefault(sequence) > 1)
            {
                throw new InvalidOperationException("wstat recovery applied one operation more than once");
            }

            if (final.State == WStatRecoveryState.Committed
                && provider.Applied.GetValueOrDefault(sequence) != 1)
            {
                throw new InvalidOperationException("committed wstat has no provider effect");
            }

            if (final.State == WStatRecoveryState.Rejected
                && provider.Applied.GetValueOrDefault(sequence) != 0)
            {
                throw new InvalidOperationException("rejected wstat changed provider state");
            }
        }
    }

    private sealed class RecoveryProvider : IFileStatOperations
    {
        private readonly Dictionary<ResourceOperationId, uint> results = new();

        internal Dictionary<ulong, RecoveryOutcome> Outcomes { get; } = new();

        internal Dictionary<ulong, int> Applied { get; } = new();

        public ValueTask<ReadOnlyMemory<byte>> StatAsync(
            ResourceHandle resource,
            uint count,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ReadOnlyMemory<byte>> StatAsync(
            ResourceOpenHandle handle,
            uint count,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<uint> WStatAsync(
            ResourceHandle resource,
            ReadOnlyMemory<byte> stat,
            ResourceOperationContext context,
            CancellationToken cancellationToken)
            => Mutate(stat, context);

        public ValueTask<uint> WStatAsync(
            ResourceOpenHandle handle,
            ReadOnlyMemory<byte> stat,
            ResourceOperationContext context,
            CancellationToken cancellationToken)
            => Mutate(stat, context);

        internal void AllowRecovery(ulong sequence)
        {
            if (Outcomes[sequence] == RecoveryOutcome.Unknown)
            {
                Outcomes[sequence] = RecoveryOutcome.Success;
            }
        }

        private ValueTask<uint> Mutate(ReadOnlyMemory<byte> stat, ResourceOperationContext context)
        {
            if (results.TryGetValue(context.OperationId, out uint prior))
            {
                return ValueTask.FromResult(prior);
            }

            RecoveryOutcome outcome = Outcomes[context.OperationId.Sequence];
            if (outcome == RecoveryOutcome.Rejected)
            {
                throw new ResourceWStatRejectedException("rejected");
            }

            if (outcome == RecoveryOutcome.Unknown)
            {
                throw new IOException("unknown outcome");
            }

            uint result = checked((uint)stat.Length);
            results.Add(context.OperationId, result);
            Applied[context.OperationId.Sequence] = Applied.GetValueOrDefault(context.OperationId.Sequence) + 1;
            if (outcome == RecoveryOutcome.LostReply)
            {
                throw new IOException("lost reply");
            }

            return ValueTask.FromResult(result);
        }
    }
}
