using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace NinePSharp.Backends.Pipes;

public class DataPipeNode : IPipeNode
{
    public string Name { get; }
    private readonly Pipe _pipe;
    private readonly CancellationTokenSource _cts = new();

    public DataPipeNode(string name)
    {
        Name = name;
        _pipe = new Pipe();
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data)
    {
        await _pipe.Writer.WriteAsync(data, _cts.Token);
    }

    public async Task<ReadOnlyMemory<byte>> ReadAsync(uint count)
    {
        var result = await _pipe.Reader.ReadAtLeastAsync((int)Math.Min(1, count), _cts.Token);
        var buffer = result.Buffer;

        // Slice the buffer to the requested count
        var length = (int)Math.Min(count, buffer.Length);
        var chunk = buffer.Slice(0, length).ToArray();

        _pipe.Reader.AdvanceTo(buffer.GetPosition(length));
        return chunk;
    }

    public void Close()
    {
        _cts.Cancel();
        _pipe.Writer.Complete();
        _pipe.Reader.Complete();
    }
}
