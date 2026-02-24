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
    public async Task<object> DispatchAsync(NinePMessage message, bool dotu, X509Certificate2? certificate = null)
    {
        var dialect = dotu ? NinePDialect.NineP2000U : NinePDialect.NineP2000;
        ushort tag = GetTag(message);
        try
        {
            if (message.IsMsgTversion)
            {
                var t = ((NinePMessage.MsgTversion)message).Item;
                return new Rversion(t.Tag, t.MSize, t.Version);
            }

            if (message.IsMsgTauth)
            {
                var t = ((NinePMessage.MsgTauth)message).Item;
                var secure = new SecureString();
                _authFids[t.Afid] = secure;
                return new Rauth(t.Tag, new Qid(QidType.QTAUTH, 0, (ulong)t.Afid));
            }

            if (message.IsMsgTattach)
            {
                var t = ((NinePMessage.MsgTattach)message).Item;
                SecureString? credentials = null;
                if (t.Afid != NinePConstants.NoFid && _authFids.TryRemove(t.Afid, out var authSec))
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
                    _fids[t.Fid] = new RootFileSystem(_backends.ToList(), _clusterManager);
                    return new Rattach(t.Tag, new Qid(QidType.QTDIR, 0, 0));
                }

                var backend = _backends.FirstOrDefault(b => b.MountPath == t.Aname || b.MountPath == "/" + t.Aname || b.Name == t.Aname);
                if (backend != null)
                {
                    var fs = backend.GetFileSystem(credentials, certificate);
                    _fids[t.Fid] = fs;
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
                            _fids[t.Fid] = new RemoteFileSystem(session.Session);
                            return new Rattach(t.Tag, new Qid(QidType.QTDIR, 0, 0));
                        }
                    }
                }

                throw new NinePProtocolException($"No backend found for aname '{t.Aname}'");
            }

            if (message.IsMsgTwalk)
            {
                var t = ((NinePMessage.MsgTwalk)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");

                // Per 9P spec: newfid must not already exist (unless newfid == fid for walk-in-place)
                if (t.NewFid != t.Fid && _fids.ContainsKey(t.NewFid))
                    return new Rerror(t.Tag, $"newfid {t.NewFid} already exists");

                var targetFs = fs.Clone();
                var response = await targetFs.WalkAsync(t);

                // Atomic Twalk: only assign newfid if walk is successful
                // Per spec: nwname = 0 means clone, always successful.
                // Otherwise, must return exactly nwname QIDs.
                if (t.Wname.Length == 0 || (response.Wqid != null && response.Wqid.Length == t.Wname.Length))
                {
                    _fids[t.NewFid] = targetFs;
                }

                return response;
            }

            if (message.IsMsgTopen)
            {
                var t = ((NinePMessage.MsgTopen)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.OpenAsync(t);
            }

            if (message.IsMsgTread)
            {
                var t = ((NinePMessage.MsgTread)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.ReadAsync(t);
            }

            if (message.IsMsgTwrite)
            {
                var t = ((NinePMessage.MsgTwrite)message).Item;
                if (_authFids.TryGetValue(t.Fid, out var secure))
                {
                    string data = System.Text.Encoding.UTF8.GetString(t.Data.ToArray());
                    foreach (char c in data) secure.AppendChar(c);
                    return new Rwrite(t.Tag, (uint)t.Data.Length);
                }
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.WriteAsync(t);
            }

            if (message.IsMsgTclunk)
            {
                var t = ((NinePMessage.MsgTclunk)message).Item;
                bool authRemoved = false;
                if (_authFids.TryRemove(t.Fid, out var secure))
                {
                    secure.Dispose();
                    authRemoved = true;
                }
                
                if (_fids.TryRemove(t.Fid, out var fs))
                {
                    return await fs.ClunkAsync(t);
                }

                if (authRemoved) return new Rclunk(t.Tag);
                return new Rerror(t.Tag, $"Unknown FID: {t.Fid}");
            }

            if (message.IsMsgTstat)
            {
                var t = ((NinePMessage.MsgTstat)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.StatAsync(t);
            }

            if (message.IsMsgTgetattr)
            {
                var t = ((NinePMessage.MsgTgetattr)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.GetAttrAsync(t);
            }

            if (message.IsMsgTsetattr)
            {
                var t = ((NinePMessage.MsgTsetattr)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.SetAttrAsync(t);
            }

            if (message.IsMsgTcreate)
            {
                var t = ((NinePMessage.MsgTcreate)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.CreateAsync(t);
            }

            if (message.IsMsgTwstat)
            {
                var t = ((NinePMessage.MsgTwstat)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.WstatAsync(t);
            }

            if (message.IsMsgTremove)
            {
                var t = ((NinePMessage.MsgTremove)message).Item;
                if (_fids.TryRemove(t.Fid, out var fs))
                {
                    return await fs.RemoveAsync(t);
                }
                throw new NinePProtocolException("Unknown FID");
            }

            // 9P2000.L Specific Additions
            if (message.IsMsgTstatfs)
            {
                var t = ((NinePMessage.MsgTstatfs)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.StatfsAsync(t);
            }

            if (message.IsMsgTlopen)
            {
                var t = ((NinePMessage.MsgTlopen)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.LopenAsync(t);
            }

            if (message.IsMsgTlcreate)
            {
                var t = ((NinePMessage.MsgTlcreate)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.LcreateAsync(t);
            }

            if (message.IsMsgTsymlink)
            {
                var t = ((NinePMessage.MsgTsymlink)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.SymlinkAsync(t);
            }

            if (message.IsMsgTmknod)
            {
                var t = ((NinePMessage.MsgTmknod)message).Item;
                if (!_fids.TryGetValue(t.Dfid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.MknodAsync(t);
            }

            if (message.IsMsgTrename)
            {
                var t = ((NinePMessage.MsgTrename)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.RenameAsync(t);
            }

            if (message.IsMsgTreadlink)
            {
                var t = ((NinePMessage.MsgTreadlink)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.ReadlinkAsync(t);
            }

            if (message.IsMsgTxattrwalk)
            {
                var t = ((NinePMessage.MsgTxattrwalk)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.XattrwalkAsync(t);
            }

            if (message.IsMsgTxattrcreate)
            {
                var t = ((NinePMessage.MsgTxattrcreate)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.XattrcreateAsync(t);
            }

            if (message.IsMsgTreaddir)
            {
                var t = ((NinePMessage.MsgTreaddir)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.ReaddirAsync(t);
            }

            if (message.IsMsgTfsync)
            {
                var t = ((NinePMessage.MsgTfsync)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.FsyncAsync(t);
            }

            if (message.IsMsgTlock)
            {
                var t = ((NinePMessage.MsgTlock)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.LockAsync(t);
            }

            if (message.IsMsgTgetlock)
            {
                var t = ((NinePMessage.MsgTgetlock)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.GetlockAsync(t);
            }

            if (message.IsMsgTlink)
            {
                var t = ((NinePMessage.MsgTlink)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.LinkAsync(t);
            }

            if (message.IsMsgTmkdir)
            {
                var t = ((NinePMessage.MsgTmkdir)message).Item;
                if (!_fids.TryGetValue(t.Dfid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.MkdirAsync(t);
            }

            if (message.IsMsgTrenameat)
            {
                var t = ((NinePMessage.MsgTrenameat)message).Item;
                if (!_fids.TryGetValue(t.OldDirFid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.RenameatAsync(t);
            }

            if (message.IsMsgTunlinkat)
            {
                var t = ((NinePMessage.MsgTunlinkat)message).Item;
                if (!_fids.TryGetValue(t.DirFd, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.UnlinkatAsync(t);
            }

            if (message.IsMsgTflush)
            {
                var t = ((NinePMessage.MsgTflush)message).Item;
                return new Rflush(t.Tag);
            }

            throw new NinePProtocolException("Message type not implemented or supported");
        }
        catch (NinePProtocolException ex)
        {
            if (dotu)
            {
                // In 9P2000.L, we return Rlerror with numerical errno
                return new Messages.Rlerror(tag, (uint)ex.ErrorCode);
            }
            return new Messages.Rerror(tag, ex.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during dispatch");
            if (dotu)
            {
                return new Messages.Rlerror(tag, 5); // EIO
            }
            return new Messages.Rerror(tag, "Internal Server Error");
        }
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
        if (message.IsMsgTgetattr) return ((NinePMessage.MsgTgetattr)message).Item.Tag;
        if (message.IsMsgTsetattr) return ((NinePMessage.MsgTsetattr)message).Item.Tag;
        if (message.IsMsgTcreate) return ((NinePMessage.MsgTcreate)message).Item.Tag;
        if (message.IsMsgTwstat) return ((NinePMessage.MsgTwstat)message).Item.Tag;
        if (message.IsMsgTremove) return ((NinePMessage.MsgTremove)message).Item.Tag;
        if (message.IsMsgTstatfs) return ((NinePMessage.MsgTstatfs)message).Item.Tag;
        if (message.IsMsgTlopen) return ((NinePMessage.MsgTlopen)message).Item.Tag;
        if (message.IsMsgTlcreate) return ((NinePMessage.MsgTlcreate)message).Item.Tag;
        if (message.IsMsgTsymlink) return ((NinePMessage.MsgTsymlink)message).Item.Tag;
        if (message.IsMsgTmknod) return ((NinePMessage.MsgTmknod)message).Item.Tag;
        if (message.IsMsgTrename) return ((NinePMessage.MsgTrename)message).Item.Tag;
        if (message.IsMsgTreadlink) return ((NinePMessage.MsgTreadlink)message).Item.Tag;
        if (message.IsMsgTxattrwalk) return ((NinePMessage.MsgTxattrwalk)message).Item.Tag;
        if (message.IsMsgTxattrcreate) return ((NinePMessage.MsgTxattrcreate)message).Item.Tag;
        if (message.IsMsgTreaddir) return ((NinePMessage.MsgTreaddir)message).Item.Tag;
        if (message.IsMsgTfsync) return ((NinePMessage.MsgTfsync)message).Item.Tag;
        if (message.IsMsgTlock) return ((NinePMessage.MsgTlock)message).Item.Tag;
        if (message.IsMsgTgetlock) return ((NinePMessage.MsgTgetlock)message).Item.Tag;
        if (message.IsMsgTlink) return ((NinePMessage.MsgTlink)message).Item.Tag;
        if (message.IsMsgTmkdir) return ((NinePMessage.MsgTmkdir)message).Item.Tag;
        if (message.IsMsgTrenameat) return ((NinePMessage.MsgTrenameat)message).Item.Tag;
        if (message.IsMsgTunlinkat) return ((NinePMessage.MsgTunlinkat)message).Item.Tag;
        if (message.IsMsgTflush) return ((NinePMessage.MsgTflush)message).Item.Tag;
        return 0;
    }
}
