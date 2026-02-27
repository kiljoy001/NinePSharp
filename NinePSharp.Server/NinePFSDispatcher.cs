using Microsoft.Extensions.Logging;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Cluster.Actors;
using Akka.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace NinePSharp.Server;

/// <summary>
/// Implements the logic for dispatching 9P messages to various backends based on session state and FID mapping.
/// </summary>
public class NinePFSDispatcher : INinePFSDispatcher
{
    private readonly ILogger<NinePFSDispatcher> _logger;
    private readonly IEnumerable<IProtocolBackend> _backends;
    private readonly IClusterManager _clusterManager;
    private readonly TimeSpan _clusterTimeout = TimeSpan.FromSeconds(3);
    private readonly ConcurrentDictionary<uint, INinePFileSystem> _fids = new();
    private readonly ConcurrentDictionary<uint, SecureString> _authFids = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<uint, INinePFileSystem>> _sessionFids = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<uint, SecureString>> _sessionAuthFids = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="NinePFSDispatcher"/> class.
    /// </summary>
    /// <param name="logger">The logger for dispatcher events.</param>
    /// <param name="backends">The collection of local backends available.</param>
    /// <param name="clusterManager">The cluster manager for remote backend discovery.</param>
    public NinePFSDispatcher(ILogger<NinePFSDispatcher> logger, IEnumerable<IProtocolBackend> backends, IClusterManager clusterManager)
    {
        _logger = logger;
        _backends = backends;
        _clusterManager = clusterManager;
    }

    /// <inheritdoc />
    public async Task<object> DispatchAsync(NinePMessage message, NinePSharp.Constants.NinePDialect dialect, X509Certificate2? certificate = null)
    {
        ushort tag = GetTag(message);
        try
        {
            return await DispatchByMessageTypeAsync(message, dialect, certificate);
        }
        catch (NinePProtocolException ex)
        {
            return CreateErrorResponse(tag, dialect, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during dispatch");
            return CreateErrorResponse(tag, dialect, new NinePProtocolException("Internal Server Error"));
        }
    }

    private async Task<object> DispatchByMessageTypeAsync(NinePMessage message, NinePSharp.Constants.NinePDialect dialect, X509Certificate2? certificate)
    {
        var connectionResponse = TryDispatchConnectionMessage(message, dialect);
        if (connectionResponse != null)
        {
            return connectionResponse;
        }

        var namespaceResponse = await TryDispatchNamespaceMessageAsync(message, certificate);
        if (namespaceResponse != null)
        {
            return namespaceResponse;
        }

        var dataResponse = await TryDispatchDataMessageAsync(message);
        if (dataResponse != null)
        {
            return dataResponse;
        }

        throw new NinePProtocolException("Message type not implemented or supported");
    }

    private object? TryDispatchConnectionMessage(NinePMessage message, NinePSharp.Constants.NinePDialect dialect)
    {
        if (message.IsMsgTversion)
        {
            var t = ((NinePMessage.MsgTversion)message).Item;
            // Reflect the negotiated version string
            string versionStr = t.Version switch
            {
                "9P2000.L" => "9P2000.L",
                "9P2000.u" => "9P2000.u",
                _ => "9P2000"
            };
            return new Rversion(t.Tag, t.MSize, versionStr);
        }

        if (message.IsMsgTauth)
        {
            var t = ((NinePMessage.MsgTauth)message).Item;
            var secure = new SecureString();
            GetAuthFidTable()[t.Afid] = secure;
            return new Rauth(t.Tag, new Qid(QidType.QTAUTH, 0, (ulong)t.Afid));
        }

        if (message.IsMsgTflush)
        {
            var t = ((NinePMessage.MsgTflush)message).Item;
            return new Rflush(t.Tag);
        }

        return null;
    }

    private async Task<object?> TryDispatchNamespaceMessageAsync(NinePMessage message, X509Certificate2? certificate)
    {
        if (message.IsMsgTattach)
        {
            return await HandleAttachAsync(((NinePMessage.MsgTattach)message).Item, certificate);
        }

        if (message.IsMsgTwalk)
        {
            return await HandleWalkAsync(((NinePMessage.MsgTwalk)message).Item);
        }

        if (message.IsMsgTclunk)
        {
            return await HandleClunkAsync(((NinePMessage.MsgTclunk)message).Item);
        }

        if (message.IsMsgTstat)
        {
            var t = ((NinePMessage.MsgTstat)message).Item;
            return await DispatchWithFsAsync(t.Fid, fs => fs.StatAsync(t));
        }

        if (message.IsMsgTcreate)
        {
            var t = ((NinePMessage.MsgTcreate)message).Item;
            return await DispatchWithFsAsync(t.Fid, fs => fs.CreateAsync(t));
        }

        if (message.IsMsgTwstat)
        {
            var t = ((NinePMessage.MsgTwstat)message).Item;
            return await DispatchWithFsAsync(t.Fid, fs => fs.WstatAsync(t));
        }

        if (message.IsMsgTremove)
        {
            return await HandleRemoveAsync(((NinePMessage.MsgTremove)message).Item);
        }

        return null;
    }

    private async Task<object?> TryDispatchDataMessageAsync(NinePMessage message)
    {
        if (message.IsMsgTopen)
        {
            var t = ((NinePMessage.MsgTopen)message).Item;
            return await DispatchWithFsAsync(t.Fid, fs => fs.OpenAsync(t));
        }

        if (message.IsMsgTread)
        {
            var t = ((NinePMessage.MsgTread)message).Item;
            return await DispatchWithFsAsync(t.Fid, fs => fs.ReadAsync(t));
        }

        if (message.IsMsgTwrite)
        {
            return await HandleWriteAsync(((NinePMessage.MsgTwrite)message).Item);
        }

        return null;
    }

    private async Task<object> HandleAttachAsync(Tattach t, X509Certificate2? certificate)
    {
        var authFids = GetAuthFidTable();
        var fids = GetFidTable();
        SecureString? credentials = null;
        if (t.Afid != NinePConstants.NoFid && authFids.TryRemove(t.Afid, out var authSec))
        {
            if (authSec.Length > 0)
            {
                credentials = authSec;
                credentials.MakeReadOnly();
            }
            else
            {
                authSec.Dispose();
            }
        }

        if (string.IsNullOrEmpty(t.Aname) || t.Aname == "/")
        {
            fids[t.Fid] = new RootFileSystem(_backends.ToList(), _clusterManager);
            return new Rattach(t.Tag, new Qid(QidType.QTDIR, 0, 0));
        }

        var backend = _backends.FirstOrDefault(b => b.MountPath == t.Aname || b.MountPath == "/" + t.Aname || b.Name == t.Aname);
        if (backend != null)
        {
            var fs = backend.GetFileSystem(credentials, certificate);
            fids[t.Fid] = fs;
            return new Rattach(t.Tag, new Qid(QidType.QTDIR, 0, 0));
        }

        if (_clusterManager.Registry != null)
        {
            var remotePath = t.Aname.StartsWith("/", StringComparison.Ordinal) ? t.Aname : "/" + t.Aname;
            var registryResponse = await _clusterManager.Registry.Ask<object>(new GetBackend(remotePath), _clusterTimeout);
            if (registryResponse is BackendFound found)
            {
                var sessionResponse = await found.Actor.Ask<object>(new SpawnSession(), _clusterTimeout);
                if (sessionResponse is SessionSpawned session)
                {
                    fids[t.Fid] = new RemoteFileSystem(session.Session);
                    return new Rattach(t.Tag, new Qid(QidType.QTDIR, 0, 0));
                }
            }
        }

        throw new NinePProtocolException($"No backend found for aname '{t.Aname}'");
    }

    private async Task<object> HandleWalkAsync(Twalk t)
    {
        var fids = GetFidTable();
        var fs = GetFileSystemOrThrow(t.Fid, fids);

        if (t.NewFid != t.Fid && fids.ContainsKey(t.NewFid))
        {
            return new Rerror(t.Tag, $"newfid {t.NewFid} already exists");
        }

        var targetFs = fs.Clone();
        var response = await targetFs.WalkAsync(t);

        if (t.Wname.Length == 0 || (response.Wqid != null && response.Wqid.Length == t.Wname.Length))
        {
            if (t.NewFid == t.Fid)
            {
                fids[t.NewFid] = targetFs;
            }
            else if (!fids.TryAdd(t.NewFid, targetFs))
            {
                return new Rerror(t.Tag, $"newfid {t.NewFid} was claimed by another thread");
            }
        }

        return response;
    }

    private async Task<object> HandleWriteAsync(Twrite t)
    {
        if (GetAuthFidTable().TryGetValue(t.Fid, out var secure))
        {
            var bytes = t.Data.Span;
            var decoder = System.Text.Encoding.UTF8.GetDecoder();
            int maxChars = System.Text.Encoding.UTF8.GetMaxCharCount(bytes.Length);
            char[] charBuffer = System.Buffers.ArrayPool<char>.Shared.Rent(maxChars);
            try
            {
                int charsDecoded = decoder.GetChars(bytes, charBuffer, true);
                for (int i = 0; i < charsDecoded; i++)
                {
                    secure.AppendChar(charBuffer[i]);
                }
            }
            finally
            {
                System.Array.Clear(charBuffer, 0, charBuffer.Length);
                System.Buffers.ArrayPool<char>.Shared.Return(charBuffer);
            }

            return new Rwrite(t.Tag, (uint)t.Data.Length);
        }

        return await DispatchWithFsAsync(t.Fid, fs => fs.WriteAsync(t));
    }

    private async Task<object> HandleClunkAsync(Tclunk t)
    {
        var authFids = GetAuthFidTable();
        var fids = GetFidTable();
        bool authRemoved = false;
        if (authFids.TryRemove(t.Fid, out var secure))
        {
            secure.Dispose();
            authRemoved = true;
        }

        if (fids.TryRemove(t.Fid, out var fs))
        {
            return await fs.ClunkAsync(t);
        }

        if (authRemoved)
        {
            return new Rclunk(t.Tag);
        }

        return new Rerror(t.Tag, $"Unknown FID: {t.Fid}");
    }

    private async Task<object> HandleRemoveAsync(Tremove t)
    {
        if (GetFidTable().TryRemove(t.Fid, out var fs))
        {
            try
            {
                return await fs.RemoveAsync(t);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backend remove failed for FID {Fid}", t.Fid);
                throw;
            }
        }

        throw new NinePProtocolException("Unknown FID");
    }

    private async Task<object> DispatchWithFsAsync<TResponse>(uint fid, Func<INinePFileSystem, Task<TResponse>> action)
        where TResponse : notnull
    {
        var fs = GetFileSystemOrThrow(fid, GetFidTable());
        return await action(fs);
    }

    private INinePFileSystem GetFileSystemOrThrow(uint fid, ConcurrentDictionary<uint, INinePFileSystem> fids)
    {
        if (!fids.TryGetValue(fid, out var fs))
        {
            throw new NinePProtocolException("Unknown FID");
        }

        return fs;
    }

    private ConcurrentDictionary<uint, INinePFileSystem> GetFidTable()
    {
        string? sessionId = NinePFSDispatcherSessionScope.CurrentSessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return _fids;
        }

        return _sessionFids.GetOrAdd(sessionId, static _ => new ConcurrentDictionary<uint, INinePFileSystem>());
    }

    private ConcurrentDictionary<uint, SecureString> GetAuthFidTable()
    {
        string? sessionId = NinePFSDispatcherSessionScope.CurrentSessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return _authFids;
        }

        return _sessionAuthFids.GetOrAdd(sessionId, static _ => new ConcurrentDictionary<uint, SecureString>());
    }

    private static object CreateErrorResponse(ushort tag, NinePDialect dialect, NinePProtocolException error)
    {
        if (dialect == NinePDialect.NineP2000L)
        {
            return new Rlerror(tag, unchecked((uint)error.ErrorCode));
        }

        return new Rerror(tag, error.ErrorMessage);
    }

    private ushort GetTag(NinePMessage message)
    {
        if (message.IsMsgTversion) return ((NinePMessage.MsgTversion)message).Item.Tag;
        if (message.IsMsgTauth) return ((NinePMessage.MsgTauth)message).Item.Tag;
        if (message.IsMsgTattach) return ((NinePMessage.MsgTattach)message).Item.Tag;
        if (message.IsMsgTwalk) return ((NinePMessage.MsgTwalk)message).Item.Tag;
        if (message.IsMsgTopen) return ((NinePMessage.MsgTopen)message).Item.Tag;
        if (message.IsMsgTread) return ((NinePMessage.MsgTread)message).Item.Tag;
        if (message.IsMsgTwrite) return ((NinePMessage.MsgTwrite)message).Item.Tag;
        if (message.IsMsgTclunk) return ((NinePMessage.MsgTclunk)message).Item.Tag;
        if (message.IsMsgTstat) return ((NinePMessage.MsgTstat)message).Item.Tag;
        if (message.IsMsgTcreate) return ((NinePMessage.MsgTcreate)message).Item.Tag;
        if (message.IsMsgTwstat) return ((NinePMessage.MsgTwstat)message).Item.Tag;
        if (message.IsMsgTremove) return ((NinePMessage.MsgTremove)message).Item.Tag;
        if (message.IsMsgTflush) return ((NinePMessage.MsgTflush)message).Item.Tag;
        return 0;
    }
}

public static class NinePFSDispatcherSessionScope
{
    private static readonly AsyncLocal<string?> SessionId = new();

    internal static string? CurrentSessionId => SessionId.Value;

    public static IDisposable Enter(string? sessionId)
    {
        string? previous = SessionId.Value;
        SessionId.Value = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public Scope(string? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            SessionId.Value = _previous;
            _disposed = true;
        }
    }
}
