using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Identity;

namespace NinePSharp.Server.FileSystem;

public class NinePFile : NinePNodeBase
{
    private byte[] content = Array.Empty<byte>();

    public NinePFile(string name)
        : base(name, QidType.QTFILE)
    {
        Mode = NinePConstants.Mode0644;
    }

    public NinePFile(string name, byte[] content)
        : this(name)
    {
        this.content = content;
        Length = (ulong)content.Length;
    }

    public override Task<byte[]> ReadAsync(ulong offset, uint count, CancellationToken ct)
    {
        if (offset >= (ulong)content.Length)
        {
            return Task.FromResult(Array.Empty<byte>());
        }

        int available = content.Length - (int)offset;
        int toRead = Math.Min((int)count, available);
        byte[] result = new byte[toRead];
        Array.Copy(content, (int)offset, result, 0, toRead);
        return Task.FromResult(result);
    }

    public override Task<uint> WriteAsync(ulong offset, byte[] data, CancellationToken ct)
    {
        if (offset == 0)
        {
            content = data;
        }
        else
        {
            if (offset + (ulong)data.Length > (ulong)content.Length)
            {
                Array.Resize(ref content, (int)offset + data.Length);
            }

            Array.Copy(data, 0, content, (int)offset, data.Length);
        }

        Length = (ulong)content.Length;
        Mtime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Task.FromResult((uint)data.Length);
    }
}
