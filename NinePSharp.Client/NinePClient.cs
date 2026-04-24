using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.IO.Pipelines;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Messages;

namespace NinePSharp.Client;

public class NinePException : Exception
{
    public NinePException(string message) : base(message) { }
}

public class NinePClient : IDisposable
{
    private readonly TcpClient? _tcpClient;
    private readonly Stream _stream;
    private readonly PipeReader _reader;
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<object>> _pendingRequests = new();
    private readonly CancellationTokenSource _cts = new();
    private ushort _nextTag = 1;
    private uint _nextFid = 1;
    private uint _msize = 8192;
    private bool _isDisposed;

    public NinePClient(string host, int port)
    {
        _tcpClient = new TcpClient(host, port);
        _stream = _tcpClient.GetStream();
        _reader = PipeReader.Create(_stream);
        _ = Task.Run(ReadLoopAsync);
    }

    public NinePClient(Stream stream)
    {
        _stream = stream;
        _reader = PipeReader.Create(_stream);
        _ = Task.Run(ReadLoopAsync);
    }

    public uint MSize => _msize;

    public async Task<RemoteFs> MountAsync(string uname = "root", string aname = "/")
    {
        await VersionAsync();
        uint rootFid = GetNextFid();
        await AttachAsync(rootFid, NinePConstants.NoFid, uname, aname);
        return new RemoteFs(this, rootFid);
    }

    public uint GetNextFid()
    {
        lock (this)
        {
            return _nextFid++;
        }
    }

    // --- Low-level API ---

    public async Task<Rversion> VersionAsync(uint msize = 8192, string version = "9P2000.L")
    {
        var tag = (ushort)NinePConstants.NoTag;
        var tversion = new Tversion(tag, msize, version);
        var response = await SendInternalAsync<Rversion>(tversion, default);
        _msize = response.MSize;
        return response;
    }

    public async Task<Rattach> AttachAsync(uint fid, uint afid, string uname, string aname)
    {
        var tag = GetNextTag();
        var tattach = new Tattach(tag, fid, afid, uname, aname);
        return await SendInternalAsync<Rattach>(tattach, default);
    }

    public async Task<Rwalk> WalkAsync(uint fid, uint newfid, string[] wnames)
    {
        var tag = GetNextTag();
        var twalk = new Twalk(tag, fid, newfid, wnames);
        return await SendInternalAsync<Rwalk>(twalk, default);
    }

    public async Task<Ropen> OpenAsync(uint fid, byte mode)
    {
        var tag = GetNextTag();
        var topen = new Topen(tag, fid, mode);
        return await SendInternalAsync<Ropen>(topen, default);
    }

    public async Task<Rread> ReadAsync(uint fid, ulong offset, uint count)
    {
        var tag = GetNextTag();
        var tread = new Tread(tag, fid, offset, count);
        return await SendInternalAsync<Rread>(tread, default);
    }

    public async Task<Rwrite> WriteAsync(uint fid, ulong offset, byte[] data)
    {
        var tag = GetNextTag();
        var twrite = new Twrite(tag, fid, offset, data);
        return await SendInternalAsync<Rwrite>(twrite, default);
    }

    public async Task<Rclunk> ClunkAsync(uint fid)
    {
        var tag = GetNextTag();
        var tclunk = new Tclunk(tag, fid);
        return await SendInternalAsync<Rclunk>(tclunk, default);
    }

    public async Task<Rstat> StatAsync(uint fid)
    {
        var tag = GetNextTag();
        var tstat = new Tstat(tag, fid);
        return await SendInternalAsync<Rstat>(tstat, default);
    }

    public async Task<Rcreate> CreateAsync(uint fid, string name, uint perm, byte mode)
    {
        var tag = GetNextTag();
        var tcreate = new Tcreate(tag, fid, name, perm, mode);
        return await SendInternalAsync<Rcreate>(tcreate, default);
    }

    public async Task<Rremove> RemoveAsync(uint fid)
    {
        var tag = GetNextTag();
        var tremove = new Tremove(tag, fid);
        return await SendInternalAsync<Rremove>(tremove, default);
    }

    public async Task<Rsymlink> SymlinkAsync(uint fid, string name, string symtgt, uint gid)
    {
        var tag = GetNextTag();
        var tsymlink = new Tsymlink(0, tag, fid, name, symtgt, gid);
        return await SendInternalAsync<Rsymlink>(tsymlink, default);
    }

    public async Task<Rreadlink> ReadlinkAsync(uint fid)
    {
        var tag = GetNextTag();
        var treadlink = new Treadlink(0, tag, fid);
        return await SendInternalAsync<Rreadlink>(treadlink, default);
    }

    public async Task<Rlink> LinkAsync(uint dfid, uint oldfid, string name)
    {
        var tag = GetNextTag();
        var tlink = new Tlink(0, tag, dfid, oldfid, name);
        return await SendInternalAsync<Rlink>(tlink, default);
    }

    public async Task FlushAsync(ushort oldTag)
    {
        var tag = GetNextTag();
        var tflush = new Tflush(tag, oldTag);
        await SendInternalAsync<Rflush>(tflush, default);
    }

    private ushort GetNextTag()
    {
        lock (this)
        {
            var tag = _nextTag++;
            if (_nextTag == NinePConstants.NoTag) _nextTag = 1;
            return tag;
        }
    }

    private async Task<T> SendInternalAsync<T>(ISerializable message, CancellationToken ct) where T : struct
    {
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(message.Tag, tcs))
        {
            throw new InvalidOperationException($"Tag {message.Tag} is already in use.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);

        try
        {
            byte[] buffer = new byte[message.Size];
            message.WriteTo(buffer);
            await _stream.WriteAsync(buffer, linkedCts.Token);
            await _stream.FlushAsync(linkedCts.Token);
            
            var result = await tcs.Task.WaitAsync(linkedCts.Token);
            
            if (result is Rerror rerror)
            {
                throw new NinePException(rerror.Ename);
            }
            if (result is Rlerror rlerror)
            {
                throw new NinePException($"Linux error code: {rlerror.Ecode}");
            }
            
            return (T)result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _ = FlushAsync(message.Tag);
            throw new NinePException($"Operation with tag {message.Tag} was cancelled and flush was sent.");
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            throw new NinePException("Operation cancelled - client is disposing");
        }
        finally
        {
            _pendingRequests.TryRemove(message.Tag, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                ReadResult result = await _reader.ReadAsync(_cts.Token);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (buffer.Length >= NinePConstants.HeaderSize)
                {
                    uint size = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4).ToArray());
                    if (buffer.Length < size) break;

                    byte type = buffer.Slice(4, 1).ToArray()[0];
                    ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(5, 2).ToArray());

                    byte[] fullMessage = buffer.Slice(0, size).ToArray();

                    object response = (MessageTypes)type switch
                    {
                        MessageTypes.Rversion => new Rversion(fullMessage),
                        MessageTypes.Rauth => new Rauth(fullMessage),
                        MessageTypes.Rattach => new Rattach(fullMessage),
                        MessageTypes.Rerror => new Rerror(fullMessage),
                        MessageTypes.Rlerror => new Rlerror(fullMessage),
                        MessageTypes.Rwalk => new Rwalk(fullMessage),
                        MessageTypes.Ropen => new Ropen(fullMessage),
                        MessageTypes.Rcreate => new Rcreate(fullMessage),
                        MessageTypes.Rread => new Rread(fullMessage),
                        MessageTypes.Rwrite => new Rwrite(fullMessage),
                        MessageTypes.Rclunk => new Rclunk(fullMessage),
                        MessageTypes.Rremove => new Rremove(fullMessage),
                        MessageTypes.Rstat => new Rstat(fullMessage),
                        MessageTypes.Rwstat => new Rwstat(fullMessage),
                        MessageTypes.Rreaddir => new Rreaddir(fullMessage),
                        MessageTypes.Rsymlink => new Rsymlink(fullMessage),
                        MessageTypes.Rreadlink => new Rreadlink(fullMessage),
                        MessageTypes.Rlink => new Rlink(fullMessage),
                        MessageTypes.Rflush => new Rflush(fullMessage),
                        _ => throw new NotSupportedException($"Unsupported message type: {(MessageTypes)type}")
                    };

                    if (_pendingRequests.TryGetValue(tag, out var tcs))
                    {
                        tcs.TrySetResult(response);
                    }

                    buffer = buffer.Slice(size);
                }

                _reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var tcs in _pendingRequests.Values)
            {
                tcs.TrySetException(ex);
            }
        }
        finally
        {
             _reader.Complete();
             foreach (var tcs in _pendingRequests.Values)
             {
                 tcs.TrySetCanceled();
             }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts.Cancel();
        _stream.Dispose();
        _tcpClient?.Dispose();
        _cts.Dispose();
    }
}
