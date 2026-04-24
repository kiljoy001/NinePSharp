using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace NinePSharp.Client.Tests;

public class LoopbackStream : Stream
{
    private readonly ChannelReader<byte> _incoming;
    private readonly ChannelWriter<byte> _outgoing;

    private LoopbackStream(ChannelReader<byte> incoming, ChannelWriter<byte> outgoing)
    {
        _incoming = incoming;
        _outgoing = outgoing;
    }

    public static (LoopbackStream Client, LoopbackStream Server) CreatePair()
    {
        var clientSource = Channel.CreateUnbounded<byte>();
        var serverSource = Channel.CreateUnbounded<byte>();

        // Client reads from serverSource, writes to clientSource
        var client = new LoopbackStream(serverSource.Reader, clientSource.Writer);
        // Server reads from clientSource, writes to serverSource
        var server = new LoopbackStream(clientSource.Reader, serverSource.Writer);
        
        return (client, server);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, default).GetAwaiter().GetResult();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            if (!_incoming.TryRead(out byte b))
            {
                if (totalRead > 0) break;
                b = await _incoming.ReadAsync(ct);
            }
            buffer[offset + totalRead] = b;
            totalRead++;

            if (_incoming.Count == 0) break;
        }
        return totalRead;
    }

    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, default).GetAwaiter().GetResult();

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        for (int i = 0; i < count; i++)
        {
            await _outgoing.WriteAsync(buffer[offset + i], ct);
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _outgoing.TryComplete();
        }
        base.Dispose(disposing);
    }
}
