using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;

namespace NinePSharp.PowerShell.Internal;

/// <summary>
/// A lightweight 9P client for internal use by PowerShell cmdlets.
/// </summary>
internal class NinePClient : IDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly Stream _stream;
    private ushort _nextTag = 1;
    private uint _msize = 8192;

    public NinePClient(string host, int port)
    {
        _tcpClient = new TcpClient(host, port);
        _stream = _tcpClient.GetStream();
    }

    public async Task VersionAsync()
    {
        var tag = _nextTag++;
        var tversion = new Tversion(tag, _msize, "9P2000.L");
        var response = await SendAsync<Rversion>(tversion);
        _msize = response.MSize;
    }

    public async Task<Qid> AttachAsync(uint fid, string user, string export)
    {
        var tag = _nextTag++;
        var tattach = new Tattach(tag, fid, NinePConstants.NoFid, user, export);
        var response = await SendAsync<Rattach>(tattach);
        return response.Qid;
    }

    public async Task<Qid[]> WalkAsync(uint fid, uint newFid, string[] path)
    {
        var tag = _nextTag++;
        var twalk = new Twalk(tag, fid, newFid, path);
        var response = await SendAsync<Rwalk>(twalk);
        return response.Wqid;
    }

    public async Task MkdirAsync(uint dfid, string name, uint mode)
    {
        var tag = _nextTag++;
        var tmkdir = new Tmkdir(0, tag, dfid, name, mode, 0);
        await SendAsync<Rmkdir>(tmkdir);
    }

    public async Task WriteAsync(uint fid, ulong offset, byte[] data)
    {
        var tag = _nextTag++;
        var twrite = new Twrite(tag, fid, offset, data);
        await SendAsync<Rwrite>(twrite);
    }

    public async Task<byte[]> ReadAsync(uint fid, ulong offset, uint count)
    {
        var tag = _nextTag++;
        var tread = new Tread(tag, fid, offset, count);
        var response = await SendAsync<Rread>(tread);
        return response.Data.ToArray();
    }

    public async Task ClunkAsync(uint fid)
    {
        var tag = _nextTag++;
        var tclunk = new Tclunk(tag, fid);
        await SendAsync<Rclunk>(tclunk);
    }

    private async Task<T> SendAsync<T>(NinePSharp.Interfaces.ISerializable message) where T : struct
    {
        byte[] buffer = new byte[message.Size];
        message.WriteTo(buffer);
        await _stream.WriteAsync(buffer);

        byte[] header = new byte[NinePConstants.HeaderSize];
        await _stream.ReadExactlyAsync(header);

        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        byte type = header[4];
        ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(5, 2));

        byte[] payload = new byte[size - (uint)NinePConstants.HeaderSize];
        if (payload.Length > 0)
        {
            await _stream.ReadExactlyAsync(payload);
        }

        byte[] fullMessage = new byte[size];
        header.CopyTo(fullMessage, 0);
        payload.CopyTo(fullMessage, NinePConstants.HeaderSize);

        if (type == (byte)MessageTypes.Rerror)
        {
            var rerror = new Rerror(fullMessage);
            throw new Exception($"9P Error: {rerror.Ename}");
        }
        if (type == (byte)MessageTypes.Rlerror)
        {
            var rlerror = new Rlerror(fullMessage);
            throw new Exception($"9P Error Code: {rlerror.Ecode}");
        }

        object result = typeof(T) switch
        {
            Type t when t == typeof(Rversion) => new Rversion(fullMessage),
            Type t when t == typeof(Rattach) => new Rattach(fullMessage),
            Type t when t == typeof(Rwalk) => new Rwalk(fullMessage),
            Type t when t == typeof(Rread) => new Rread(fullMessage),
            Type t when t == typeof(Rwrite) => new Rwrite(fullMessage),
            Type t when t == typeof(Rmkdir) => new Rmkdir(fullMessage),
            _ => throw new NotSupportedException($"Unsupported response type: {typeof(T).Name}")
        };

        return (T)result;
    }

    public void Dispose()
    {
        _stream.Dispose();
        _tcpClient.Dispose();
    }
}
