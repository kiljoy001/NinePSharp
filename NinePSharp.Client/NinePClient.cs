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
    private static readonly IReadOnlyDictionary<MessageTypes, Func<byte[], object>> ResponseParsers =
        new Dictionary<MessageTypes, Func<byte[], object>>
        {
            [MessageTypes.Rversion] = message => new Rversion(message),
            [MessageTypes.Rauth] = message => new Rauth(message),
            [MessageTypes.Rattach] = message => new Rattach(message),
            [MessageTypes.Rerror] = message => new Rerror(message),
            [MessageTypes.Rlerror] = message => new Rlerror(message),
            [MessageTypes.Rwalk] = message => new Rwalk(message),
            [MessageTypes.Ropen] = message => new Ropen(message),
            [MessageTypes.Rcreate] = message => new Rcreate(message),
            [MessageTypes.Rread] = message => new Rread(message),
            [MessageTypes.Rwrite] = message => new Rwrite(message),
            [MessageTypes.Rclunk] = message => new Rclunk(message),
            [MessageTypes.Rremove] = message => new Rremove(message),
            [MessageTypes.Rstat] = message => new Rstat(message),
            [MessageTypes.Rwstat] = message => new Rwstat(message),
            [MessageTypes.Rreaddir] = message => new Rreaddir(message),
            [MessageTypes.Rsymlink] = message => new Rsymlink(message),
            [MessageTypes.Rreadlink] = message => new Rreadlink(message),
            [MessageTypes.Rlink] = message => new Rlink(message),
            [MessageTypes.Rflush] = message => new Rflush(message),
        };

    private readonly TcpClient? _tcpClient;
    private readonly Stream _stream;
    private readonly PipeReader _reader;
    private readonly object _stateGate = new();
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<object>> _pendingRequests = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readLoopTask;
    private ushort _nextTag = 1;
    private uint _nextFid = 1;
    private uint _msize = 8192;
    private bool _isDisposed;

    public NinePClient(string host, int port)
    {
        _tcpClient = new TcpClient(host, port);
        _stream = _tcpClient.GetStream();
        _reader = PipeReader.Create(_stream);
        _readLoopTask = Task.Run(ReadLoopAsync);
    }

    public NinePClient(Stream stream)
    {
        _stream = stream;
        _reader = PipeReader.Create(_stream);
        _readLoopTask = Task.Run(ReadLoopAsync);
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
        lock (_stateGate)
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
        lock (_stateGate)
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

    private static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out byte[] fullMessage, out MessageTypes type, out ushort tag)
    {
        fullMessage = Array.Empty<byte>();
        type = default;
        tag = default;

        if (buffer.Length < NinePConstants.HeaderSize)
        {
            return false;
        }

        var header = buffer.Slice(0, NinePConstants.HeaderSize).ToArray();
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        if (buffer.Length < size)
        {
            return false;
        }

        type = (MessageTypes)header[4];
        tag = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(5, 2));
        fullMessage = buffer.Slice(0, size).ToArray();
        buffer = buffer.Slice(size);
        return true;
    }

    private static object ParseResponse(MessageTypes type, byte[] fullMessage)
    {
        if (ResponseParsers.TryGetValue(type, out var parser))
        {
            return parser(fullMessage);
        }

        throw new NotSupportedException($"Unsupported message type: {type}");
    }

    private void CompletePendingRequest(ushort tag, object response)
    {
        if (_pendingRequests.TryGetValue(tag, out var tcs))
        {
            tcs.TrySetResult(response);
        }
    }

    private void FailPendingRequests(Exception exception)
    {
        foreach (var tcs in _pendingRequests.Values)
        {
            tcs.TrySetException(exception);
        }
    }

    private void CancelPendingRequests()
    {
        foreach (var tcs in _pendingRequests.Values)
        {
            tcs.TrySetCanceled();
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

                while (TryReadFrame(ref buffer, out var fullMessage, out var type, out var tag))
                {
                    CompletePendingRequest(tag, ParseResponse(type, fullMessage));
                }

                _reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            FailPendingRequests(ex);
        }
        finally
        {
             _reader.Complete();
             CancelPendingRequests();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts.Cancel();
        _stream.Dispose();
        _tcpClient?.Dispose();
        try
        {
            _readLoopTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        _cts.Dispose();
    }
}
