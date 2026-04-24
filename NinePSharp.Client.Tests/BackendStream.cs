using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;
using NinePSharp.Interfaces;
using NinePSharp.Parser;

namespace NinePSharp.Client.Tests;

// A stream that dispatches directly to an INinePRequestHandler
public class BackendStream : Stream
{
    private readonly INinePRequestHandler _handler;
    private readonly MemoryStream _incoming = new();
    private readonly object _lock = new();

    public BackendStream(INinePRequestHandler handler)
    {
        _handler = handler;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, default).GetAwaiter().GetResult();

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var data = new byte[count];
        Array.Copy(buffer, offset, data, 0, count);
        
        // Parse message
        // For simplicity, let's assume we are in net10.0 and can use the parser
        var msgResult = NinePParser.parse(NinePDialect.NineP2000L, data.AsMemory());
        if (msgResult.IsError) return;

        object? response = null;
        var msg = msgResult.ResultValue;
        
        // This is a minimal dispatcher for the test stream
        if (msg is NinePMessage.MsgTversion t) response = new Rversion(t.Item.Tag, 8192, "9P2000.L");
        else if (msg is NinePMessage.MsgTattach a) response = await _handler.AttachAsync(a.Item, ct);
        else if (msg is NinePMessage.MsgTwalk w) response = await _handler.WalkAsync(Array.Empty<string>(), w.Item, ct);
        else if (msg is NinePMessage.MsgTopen o) response = await _handler.OpenAsync(Array.Empty<string>(), o.Item, ct);
        else if (msg is NinePMessage.MsgTread r) response = await _handler.ReadAsync(new[] { "hello.txt" }, r.Item, ct);
        else if (msg is NinePMessage.MsgTclunk c) response = await _handler.ClunkAsync(new[] { "hello.txt" }, c.Item, ct);

        if (response is ISerializable serializable)
        {
            byte[] respBuf = new byte[serializable.Size];
            serializable.WriteTo(respBuf);
            lock (_lock)
            {
                _incoming.Write(respBuf);
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            _incoming.Position = 0;
            int read = _incoming.Read(buffer, offset, count);
            // Clear stream after read
            _incoming.SetLength(0);
            return read;
        }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        while (true)
        {
            lock (_lock)
            {
                if (_incoming.Length > 0)
                {
                    _incoming.Position = 0;
                    int read = _incoming.Read(buffer, offset, count);
                    _incoming.SetLength(0);
                    return read;
                }
            }
            await Task.Delay(10, ct);
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
