using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NinePSharp.Backends.Pipes;

public interface IPipeNode
{
    string Name { get; }
    Task WriteAsync(ReadOnlyMemory<byte> data);
    Task<ReadOnlyMemory<byte>> ReadAsync(uint count);
    void Close();
}

public class ObjectQueueNode : IPipeNode
{
    public string Name { get; }
    private readonly Channel<byte[]> _channel;
    private readonly CancellationTokenSource _cts = new();

    public ObjectQueueNode(string name, int capacity = 1000)
    {
        Name = name;
        // Bounded channel provides backpressure
        _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data)
    {
        await _channel.Writer.WriteAsync(data.ToArray(), _cts.Token);
    }

    public async Task<ReadOnlyMemory<byte>> ReadAsync(uint count)
    {
        // For Object Queues, we return exactly one message per read
        // regardless of the 'count' requested (standard MQ semantics)
        var msg = await _channel.Reader.ReadAsync(_cts.Token);
        return msg;
    }

    public void Close()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
    }
}
