using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Interfaces;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;

namespace NinePSharp.Namespaces.Orleans.Server;

/// <summary>Dispatches 9P2000-family requests to connection-local fids backed by Orleans resources.</summary>
public sealed class DistributedNamespaceDispatcher : INinePFSDispatcher, INinePSessionLifecycle
{
    private readonly ConcurrentDictionary<string, SessionHolder> sessions = new(StringComparer.Ordinal);
    private readonly DistributedNamespaceOperations operations;
    private readonly IDistributedNamespaceAttachResolver attachResolver;
    private readonly uint maximumMessageSize;

    /// <summary>Initializes a distributed namespace dispatcher.</summary>
    public DistributedNamespaceDispatcher(
        DistributedNamespaceOperations operations,
        IDistributedNamespaceAttachResolver attachResolver,
        uint maximumMessageSize = 1024 * 1024)
    {
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        this.attachResolver = attachResolver ?? throw new ArgumentNullException(nameof(attachResolver));
        if (maximumMessageSize < 256 || maximumMessageSize > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumMessageSize));
        }

        this.maximumMessageSize = maximumMessageSize;
    }

    /// <inheritdoc/>
    public async Task<object> DispatchAsync(
        string sessionId,
        NinePMessage message,
        NinePDialect dialect,
        X509Certificate2? certificate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(message);
        ushort tag = GetTag(message);

        if (message is NinePMessage.MsgTversion version)
        {
            await CloseSessionAsync(sessionId);
            if (version.Item.MSize < 256)
            {
                return Error(tag, dialect, new ArgumentException("msize must be at least 256 bytes"));
            }

            uint size = Math.Min(version.Item.MSize, maximumMessageSize);
            sessions[sessionId] = new SessionHolder { MessageSize = size };
            return new Rversion(version.Item.Tag, size, NegotiateVersion(version.Item.Version));
        }

        SessionHolder holder = sessions.GetOrAdd(sessionId, static _ => new SessionHolder());
        if (message is NinePMessage.MsgTflush flush)
        {
            return await FlushAsync(holder, flush.Item);
        }

        var inFlight = new InFlightRequest();
        if (!holder.InFlight.TryAdd(tag, inFlight))
        {
            inFlight.DisposeCancellation();
            return Error(tag, dialect, new NamespaceFidException("duplicate tag"));
        }

        try
        {
            object response = await DispatchCoreAsync(
                sessionId,
                holder,
                LimitReadCount(message, holder.MessageSize),
                dialect,
                certificate,
                inFlight.Cancellation.Token);
            return response is ISerializable serializable && serializable.Size > holder.MessageSize
                ? Error(tag, dialect, new IOException("response exceeds negotiated msize"))
                : response;
        }
        catch (Exception exception)
        {
            return Error(tag, dialect, exception);
        }
        finally
        {
            inFlight.Completion.TrySetResult();
            holder.InFlight.TryRemove(tag, out _);
            inFlight.DisposeCancellation();
        }
    }

    private static NinePMessage LimitReadCount(NinePMessage message, uint messageSize)
    {
        uint count = messageSize - NinePConstants.HeaderSize - 4;
        return message switch
        {
            NinePMessage.MsgTread read => NinePMessage.NewMsgTread(
                new Tread(read.Item.Tag, read.Item.Fid, read.Item.Offset, Math.Min(read.Item.Count, count))),
            NinePMessage.MsgTreaddir read => NinePMessage.NewMsgTreaddir(
                new Treaddir(read.Item.Size, read.Item.Tag, read.Item.Fid, read.Item.Offset, Math.Min(read.Item.Count, count))),
            _ => message,
        };
    }

    /// <inheritdoc/>
    public async Task CloseSessionAsync(string sessionId)
    {
        if (!sessions.TryRemove(sessionId, out SessionHolder? holder))
        {
            return;
        }

        InFlightRequest[] requests = holder.InFlight.Values.ToArray();
        foreach (InFlightRequest request in requests)
        {
            request.Cancel();
        }

        await Task.WhenAll(requests.Select(request => request.Completion.Task));
        if (holder.Session is not null)
        {
            await holder.Session.DisposeAsync();
        }

        holder.Initialization.Dispose();
    }

    private Task<object> DispatchCoreAsync(
        string sessionId,
        SessionHolder holder,
        NinePMessage message,
        NinePDialect dialect,
        X509Certificate2? certificate,
        CancellationToken cancellationToken)
        => message is NinePMessage.MsgTattach attach
            ? AttachAsync(sessionId, holder, attach.Item, dialect, certificate, cancellationToken)
            : DispatchFidRequestAsync(RequireSession(holder), message, dialect, cancellationToken);

    private static Task<object> DispatchFidRequestAsync(
        NamespaceSession session,
        NinePMessage message,
        NinePDialect dialect,
        CancellationToken cancellationToken)
        => message switch
        {
            NinePMessage.MsgTwalk walk => WalkAsync(session, walk.Item, cancellationToken),
            NinePMessage.MsgTopen open => OpenAsync(session, open.Item, cancellationToken),
            NinePMessage.MsgTread read => ReadAsync(session, read.Item, dialect, cancellationToken),
            NinePMessage.MsgTwrite write => WriteAsync(session, write.Item, cancellationToken),
            _ => DispatchFidMutationAsync(session, message, dialect, cancellationToken),
        };

    private static Task<object> DispatchFidMutationAsync(
        NamespaceSession session,
        NinePMessage message,
        NinePDialect dialect,
        CancellationToken cancellationToken)
        => message switch
        {
            NinePMessage.MsgTstat stat => StatAsync(session, stat.Item, dialect, cancellationToken),
            NinePMessage.MsgTcreate create => CreateAsync(session, create.Item, cancellationToken),
            NinePMessage.MsgTclunk clunk => ClunkAsync(session, clunk.Item, cancellationToken),
            NinePMessage.MsgTremove remove => RemoveAsync(session, remove.Item, cancellationToken),
            _ => DispatchLinuxAsync(session, message, cancellationToken),
        };

    private static Task<object> DispatchLinuxAsync(
        NamespaceSession session,
        NinePMessage message,
        CancellationToken cancellationToken)
        => message switch
        {
            NinePMessage.MsgTlopen open => LinuxOpenAsync(session, open.Item, cancellationToken),
            NinePMessage.MsgTlcreate create => LinuxCreateAsync(session, create.Item, cancellationToken),
            NinePMessage.MsgTreaddir readdir => LinuxReadDirectoryAsync(session, readdir.Item, cancellationToken),
            NinePMessage.MsgTgetattr getattr => LinuxGetAttrAsync(session, getattr.Item, cancellationToken),
            _ => Task.FromException<object>(
                new NotSupportedException("The request is not implemented by the namespace dispatcher.")),
        };

    private static NamespaceSession RequireSession(SessionHolder holder)
        => holder.Session ?? throw new NamespaceFidException("unknown fid");

    private static async Task<object> WalkAsync(
        NamespaceSession session,
        Twalk request,
        CancellationToken cancellationToken)
    {
        NamespaceWalkResult result = await session.WalkAsync(
            request.Fid,
            request.NewFid,
            request.Wname,
            cancellationToken);
        if (request.Wname.Length > 0 && result.Qids.Count == 0)
        {
            throw new FileNotFoundException("file does not exist");
        }

        return new Rwalk(request.Tag, result.Qids.ToArray());
    }

    private static async Task<object> OpenAsync(
        NamespaceSession session,
        Topen request,
        CancellationToken cancellationToken)
    {
        ResourceOpenHandle result = await session.OpenAsync(request.Fid, request.Mode, cancellationToken);
        return new Ropen(request.Tag, result.Resource.Qid, result.IoUnit);
    }

    private static async Task<object> ReadAsync(
        NamespaceSession session,
        Tread request,
        NinePDialect dialect,
        CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> data = session.GetFidResource(request.Fid).IsDirectory
            ? EncodeStatDirectory(
                await session.ReadDirectoryAsync(request.Fid, cancellationToken),
                dialect,
                request.Offset,
                request.Count)
            : await session.ReadAsync(request.Fid, request.Offset, request.Count, cancellationToken);
        return new Rread(request.Tag, data);
    }

    private static async Task<object> WriteAsync(
        NamespaceSession session,
        Twrite request,
        CancellationToken cancellationToken)
    {
        uint count = await session.WriteAsync(request.Fid, request.Offset, request.Data, cancellationToken);
        return new Rwrite(request.Tag, count);
    }

    private static async Task<object> StatAsync(
        NamespaceSession session,
        Tstat request,
        NinePDialect dialect,
        CancellationToken cancellationToken)
    {
        ResourceStat result = await session.StatAsync(request.Fid, cancellationToken);
        return new Rstat(request.Tag, ToStat(result, dialect));
    }

    private static async Task<object> CreateAsync(
        NamespaceSession session,
        Tcreate request,
        CancellationToken cancellationToken)
    {
        ResourceOpenHandle result = await session.CreateAsync(
            request.Fid,
            request.Name,
            request.Perm,
            request.Mode,
            cancellationToken);
        return new Rcreate(request.Tag, result.Resource.Qid, result.IoUnit);
    }

    private static async Task<object> ClunkAsync(
        NamespaceSession session,
        Tclunk request,
        CancellationToken cancellationToken)
    {
        await session.ClunkAsync(request.Fid, cancellationToken);
        return new Rclunk(request.Tag);
    }

    private static async Task<object> RemoveAsync(
        NamespaceSession session,
        Tremove request,
        CancellationToken cancellationToken)
    {
        await session.RemoveAsync(request.Fid, cancellationToken);
        return new Rremove(request.Tag);
    }

    private static async Task<object> LinuxOpenAsync(
        NamespaceSession session,
        Tlopen request,
        CancellationToken cancellationToken)
    {
        byte mode = LinuxProtocol.ToOpenMode(request.Flags);
        bool requireDirectory = LinuxProtocol.RequiresDirectory(request.Flags);
        ResourceOpenHandle result = await session.OpenAsync(
            request.Fid,
            mode,
            requireDirectory,
            cancellationToken);
        return new Rlopen(24, request.Tag, result.Resource.Qid, result.IoUnit);
    }

    private static async Task<object> LinuxCreateAsync(
        NamespaceSession session,
        Tlcreate request,
        CancellationToken cancellationToken)
    {
        ResourceOpenHandle result = await session.CreateAsync(
            request.Fid,
            request.Name,
            request.Mode & 0xFFF,
            LinuxProtocol.ToOpenMode(request.Flags, creating: true),
            cancellationToken);
        return new Rlcreate(24, request.Tag, result.Resource.Qid, result.IoUnit);
    }

    private static async Task<object> LinuxReadDirectoryAsync(
        NamespaceSession session,
        Treaddir request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ResourceStat> entries = await session.ReadDirectoryAsync(request.Fid, cancellationToken);
        ReadOnlyMemory<byte> data = LinuxProtocol.EncodeDirectory(entries, request.Offset, request.Count);
        return new Rreaddir(
            checked((uint)(NinePConstants.HeaderSize + 4 + data.Length)),
            request.Tag,
            checked((uint)data.Length),
            data);
    }

    private static async Task<object> LinuxGetAttrAsync(
        NamespaceSession session,
        Tgetattr request,
        CancellationToken cancellationToken)
    {
        ResourceStat stat = await session.StatAsync(request.Fid, cancellationToken);
        return LinuxProtocol.ToGetAttr(request, stat);
    }

    private async Task<object> AttachAsync(
        string sessionId,
        SessionHolder holder,
        Tattach request,
        NinePDialect dialect,
        X509Certificate2? certificate,
        CancellationToken cancellationToken)
    {
        await holder.Initialization.WaitAsync(cancellationToken);
        try
        {
            DistributedNamespaceAttach descriptor = await attachResolver.ResolveAsync(
                sessionId,
                request,
                dialect,
                certificate,
                cancellationToken);
            if (holder.Session is null)
            {
                holder.Descriptor = descriptor;
                holder.Session = new NamespaceSession(
                    holder.OperationSessionId,
                    descriptor.ProcessId,
                    descriptor.User,
                    new DistributedNamespaceDataPlane(descriptor.ProcessGroupId, operations));
            }
            else if (!SameNamespace(holder.Descriptor!, descriptor))
            {
                throw new InvalidOperationException("A connection cannot attach to different process namespaces.");
            }

            ResourceHandle root = await holder.Session.AttachAsync(request.Fid, descriptor.Root, cancellationToken);
            return new Rattach(request.Tag, root.Qid);
        }
        finally
        {
            holder.Initialization.Release();
        }
    }

    private static async Task<object> FlushAsync(SessionHolder holder, Tflush request)
    {
        if (holder.InFlight.TryGetValue(request.OldTag, out InFlightRequest? oldRequest))
        {
            oldRequest.Cancel();
            await oldRequest.Completion.Task;
        }

        return new Rflush(request.Tag);
    }

    private static bool SameNamespace(DistributedNamespaceAttach left, DistributedNamespaceAttach right)
        => left.ProcessGroupId == right.ProcessGroupId
            && left.ProcessId == right.ProcessId
            && left.User == right.User;

    private static ReadOnlyMemory<byte> EncodeStatDirectory(
        IReadOnlyList<ResourceStat> entries,
        NinePDialect dialect,
        ulong requestedOffset,
        uint count)
    {
        ulong position = 0;
        int startIndex = 0;
        while (startIndex < entries.Count && position < requestedOffset)
        {
            position = checked(position + ToStat(entries[startIndex], dialect).Size);
            startIndex++;
        }

        if (position < requestedOffset)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (position != requestedOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedOffset), "offset is not a directory boundary");
        }

        int budget = checked((int)Math.Min(count, int.MaxValue));
        int length = 0;
        int endIndex = startIndex;
        while (endIndex < entries.Count)
        {
            int entryLength = ToStat(entries[endIndex], dialect).Size;
            if (entryLength > budget - length)
            {
                break;
            }

            length += entryLength;
            endIndex++;
        }

        byte[] data = new byte[length];
        int offset = 0;
        for (int index = startIndex; index < endIndex; index++)
        {
            Stat stat = ToStat(entries[index], dialect);
            stat.WriteTo(data, ref offset);
        }

        return data;
    }

    private static Stat ToStat(ResourceStat stat, NinePDialect dialect)
        => new(
            0,
            0,
            0,
            stat.Resource.Qid,
            stat.Mode,
            stat.AccessTime,
            stat.ModificationTime,
            stat.Length,
            stat.Name,
            stat.User,
            stat.Group,
            stat.LastModifier,
            dialect);

    private static object Error(ushort tag, NinePDialect dialect, Exception exception)
    {
        string message = ErrorMessage(exception);
        uint code = ErrorCode(exception, message);
        if (Encoding.UTF8.GetByteCount(message) > 200)
        {
            message = "resource operation failed";
        }

        return dialect == NinePDialect.NineP2000L
            ? new Rlerror(tag, code)
            : new Rerror(tag, message, dialect == NinePDialect.NineP2000U ? code : null);
    }

    private static uint ErrorCode(Exception exception, string message)
    {
        if (exception is OperationCanceledException)
        {
            return (uint)LinuxErrorCode.ECANCELED;
        }

        if (exception is NamespaceFidException)
        {
            return NamespaceErrorCode(message);
        }

        return ProviderErrorCode(exception);
    }

    private static uint ProviderErrorCode(Exception exception)
        => exception switch
        {
            NotSupportedException => (uint)LinuxErrorCode.EOPNOTSUPP,
            FileNotFoundException => (uint)LinuxErrorCode.ENOENT,
            DirectoryNotFoundException => (uint)LinuxErrorCode.ENOTDIR,
            UnauthorizedAccessException => (uint)LinuxErrorCode.EACCESS,
            IOException => (uint)LinuxErrorCode.EIO,
            _ => (uint)LinuxErrorCode.EINVAL,
        };

    private static uint NamespaceErrorCode(string message)
    {
        if (message.Contains("unknown fid", StringComparison.Ordinal))
        {
            return (uint)LinuxErrorCode.EBADF;
        }

        if (message.Contains("duplicate", StringComparison.Ordinal))
        {
            return (uint)LinuxErrorCode.EEXIST;
        }

        return NamespaceTypeErrorCode(message);
    }

    private static uint NamespaceTypeErrorCode(string message)
    {
        if (message.Contains("not a directory", StringComparison.Ordinal)
            || message.Contains("non-directory", StringComparison.Ordinal))
        {
            return (uint)LinuxErrorCode.ENOTDIR;
        }

        return message.Contains("only be opened", StringComparison.Ordinal)
            ? (uint)LinuxErrorCode.EISDIR
            : (uint)LinuxErrorCode.EINVAL;
    }

    private static string ErrorMessage(Exception exception)
        => exception is OperationCanceledException ? "interrupted" : exception.Message;

    private static ushort GetTag(NinePMessage message)
    {
        if (TryGetSessionTag(message, out ushort tag)
            || TryGetClassicTag(message, out tag)
            || TryGetLinuxOpenTag(message, out tag)
            || TryGetLinuxMetadataTag(message, out tag)
            || TryGetLinuxIoTag(message, out tag)
            || TryGetLinuxControlTag(message, out tag)
            || TryGetLinuxPathTag(message, out tag))
        {
            return tag;
        }

        return NinePConstants.NoTag;
    }

    private static bool TryGetSessionTag(NinePMessage message, out ushort tag)
    {
        tag = message switch
        {
            NinePMessage.MsgTversion value => value.Item.Tag,
            NinePMessage.MsgTauth value => value.Item.Tag,
            NinePMessage.MsgTattach value => value.Item.Tag,
            NinePMessage.MsgTwalk value => value.Item.Tag,
            NinePMessage.MsgTopen value => value.Item.Tag,
            NinePMessage.MsgTflush value => value.Item.Tag,
            _ => NinePConstants.NoTag,
        };
        return tag != NinePConstants.NoTag;
    }

    private static bool TryGetClassicTag(NinePMessage message, out ushort tag)
    {
        tag = message switch
        {
            NinePMessage.MsgTread value => value.Item.Tag,
            NinePMessage.MsgTwrite value => value.Item.Tag,
            NinePMessage.MsgTclunk value => value.Item.Tag,
            NinePMessage.MsgTstat value => value.Item.Tag,
            NinePMessage.MsgTcreate value => value.Item.Tag,
            NinePMessage.MsgTwstat value => value.Item.Tag,
            NinePMessage.MsgTremove value => value.Item.Tag,
            _ => NinePConstants.NoTag,
        };
        return tag != NinePConstants.NoTag;
    }

    private static bool TryGetLinuxOpenTag(NinePMessage message, out ushort tag)
    {
        tag = message switch
        {
            NinePMessage.MsgTstatfs value => value.Item.Tag,
            NinePMessage.MsgTlopen value => value.Item.Tag,
            NinePMessage.MsgTlcreate value => value.Item.Tag,
            NinePMessage.MsgTsymlink value => value.Item.Tag,
            _ => NinePConstants.NoTag,
        };
        return tag != NinePConstants.NoTag;
    }

    private static bool TryGetLinuxMetadataTag(NinePMessage message, out ushort tag)
    {
        tag = message switch
        {
            NinePMessage.MsgTmknod value => value.Item.Tag,
            NinePMessage.MsgTrename value => value.Item.Tag,
            NinePMessage.MsgTreaddir value => value.Item.Tag,
            NinePMessage.MsgTreadlink value => value.Item.Tag,
            _ => NinePConstants.NoTag,
        };
        return tag != NinePConstants.NoTag;
    }

    private static bool TryGetLinuxIoTag(NinePMessage message, out ushort tag)
    {
        tag = message switch
        {
            NinePMessage.MsgTgetattr value => value.Item.Tag,
            NinePMessage.MsgTsetattr value => value.Item.Tag,
            NinePMessage.MsgTxattrwalk value => value.Item.Tag,
            NinePMessage.MsgTxattrcreate value => value.Item.Tag,
            _ => NinePConstants.NoTag,
        };
        return tag != NinePConstants.NoTag;
    }

    private static bool TryGetLinuxControlTag(NinePMessage message, out ushort tag)
    {
        tag = message switch
        {
            NinePMessage.MsgTfsync value => value.Item.Tag,
            NinePMessage.MsgTlock value => value.Item.Tag,
            NinePMessage.MsgTgetlock value => value.Item.Tag,
            NinePMessage.MsgTlink value => value.Item.Tag,
            _ => NinePConstants.NoTag,
        };
        return tag != NinePConstants.NoTag;
    }

    private static bool TryGetLinuxPathTag(NinePMessage message, out ushort tag)
    {
        tag = message switch
        {
            NinePMessage.MsgTmkdir value => value.Item.Tag,
            NinePMessage.MsgTrenameat value => value.Item.Tag,
            NinePMessage.MsgTunlinkat value => value.Item.Tag,
            _ => NinePConstants.NoTag,
        };
        return tag != NinePConstants.NoTag;
    }

    private static string NegotiateVersion(string requested)
        => requested switch
        {
            NinePConstants.VersionString_9pl => NinePConstants.VersionString_9pl,
            NinePConstants.VersionString_9pu => NinePConstants.VersionString_9pu,
            _ => requested.StartsWith("9P", StringComparison.Ordinal) ? NinePConstants.VersionString_9p : "unknown",
        };

    private sealed class SessionHolder
    {
        internal uint MessageSize { get; init; } = 8192;

        internal string OperationSessionId { get; } = Guid.NewGuid().ToString("N");

        internal SemaphoreSlim Initialization { get; } = new(1, 1);

        internal ConcurrentDictionary<ushort, InFlightRequest> InFlight { get; } = new();

        internal DistributedNamespaceAttach? Descriptor { get; set; }

        internal NamespaceSession? Session { get; set; }
    }

    private sealed class InFlightRequest
    {
        private readonly object gate = new();
        private bool disposed;

        internal CancellationTokenSource Cancellation { get; } = new();

        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Cancel()
        {
            lock (gate)
            {
                if (!disposed)
                {
                    Cancellation.Cancel();
                }
            }
        }

        internal void DisposeCancellation()
        {
            lock (gate)
            {
                disposed = true;
                Cancellation.Dispose();
            }
        }
    }
}
