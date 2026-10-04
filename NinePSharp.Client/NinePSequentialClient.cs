using System.Buffers.Binary;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Messages;

namespace NinePSharp.Client;

/// <summary>Bounded sequential 9P2000 exchanges for control services. Any ambiguous IO terminates the stream.</summary>
public sealed class NinePSequentialClient : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly uint maximumMessageSize;
    private uint messageSize;
    private ushort nextTag;
    private int terminated;
    private bool negotiated;

    public NinePSequentialClient(Stream stream, uint maximumMessageSize = 8192)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maximumMessageSize < 256 || maximumMessageSize > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumMessageSize));
        }

        this.stream = stream;
        this.maximumMessageSize = maximumMessageSize;
        messageSize = maximumMessageSize;
    }

    public uint MessageSize => messageSize;

    public async Task NegotiateAsync(CancellationToken cancellationToken)
    {
        var version = await ExchangeAsync<Rversion>(_ => new Tversion(65535, maximumMessageSize, "9P2000"), cancellationToken).ConfigureAwait(false);
        if (version.Version != "9P2000" || version.MSize < 256 || version.MSize > maximumMessageSize)
        {
            await DisposeAsync().ConfigureAwait(false);
            throw new IOException("Invalid 9P negotiation.");
        }

        messageSize = version.MSize;
        negotiated = true;
    }

    public async Task<T> ExchangeAsync<T>(Func<ushort, ISerializable> createRequest, CancellationToken cancellationToken)
        where T : struct, ISerializable
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref terminated) != 0, this);
            ushort tag = typeof(T) == typeof(Rversion) ? (ushort)65535 : nextTag++;
            if (nextTag == 65535)
            {
                nextTag = 0;
            }

            byte[] frame = SerializeRequest(createRequest(tag), tag);
            bool answered = false;
            try
            {
                object parsed = ParseResponse<T>(await RoundTripAsync(frame, tag, cancellationToken).ConfigureAwait(false));
                answered = true;
                return parsed is Rerror error ? throw new NinePException(error.Ename) : (T)parsed;
            }
            finally
            {
                // An Rerror answers the request; anything else that fails leaves the stream ambiguous.
                if (!answered)
                {
                    await DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref terminated, 1) == 0)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static object ParseResponse<T>(byte[] response)
        where T : struct, ISerializable
    {
        object parsed = (MessageTypes)response[4] switch
        {
            MessageTypes.Rversion => new Rversion(response), MessageTypes.Rauth => new Rauth(response),
            MessageTypes.Rattach => new Rattach(response), MessageTypes.Rwalk => new Rwalk(response),
            MessageTypes.Ropen => new Ropen(response), MessageTypes.Rread => new Rread(response),
            MessageTypes.Rwrite => new Rwrite(response), MessageTypes.Rclunk => new Rclunk(response),
            MessageTypes.Rstat => new Rstat(response), MessageTypes.Rflush => new Rflush(response),
            MessageTypes.Rerror => new Rerror(response), _ => throw new IOException("Unexpected 9P response."),
        };
        ISerializable serializable = (ISerializable)parsed;
        byte[] canonical = new byte[serializable.Size];
        serializable.WriteTo(canonical);
        if (!response.AsSpan().SequenceEqual(canonical) || (parsed is not T && parsed is not Rerror))
        {
            throw new IOException("Noncanonical or mismatched 9P response.");
        }

        return parsed;
    }

    private byte[] SerializeRequest(ISerializable request, ushort tag)
    {
        if (!negotiated && request.Type != MessageTypes.Tversion)
        {
            throw new InvalidOperationException("Negotiate before exchanging requests.");
        }

        if (request.Tag != tag || request.Size < 7 || request.Size > messageSize)
        {
            throw new ArgumentException("Invalid request frame.");
        }

        byte[] frame = new byte[request.Size];
        request.WriteTo(frame);
        return frame;
    }

    private async Task<byte[]> RoundTripAsync(byte[] frame, ushort tag, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        byte[] header = new byte[7];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (size < 7 || size > messageSize || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(5)) != tag)
        {
            throw new IOException("Invalid response frame.");
        }

        byte[] response = new byte[size];
        header.CopyTo(response, 0);
        await stream.ReadExactlyAsync(response.AsMemory(7), cancellationToken).ConfigureAwait(false);
        return response;
    }
}
