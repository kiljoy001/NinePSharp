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
using System.Threading.Tasks;

namespace NinePSharp.Server;

public class NinePFSDispatcher : INinePFSDispatcher
{
    private readonly ILogger<NinePFSDispatcher> _logger;
    private readonly IEnumerable<IProtocolBackend> _backends;
    private readonly IClusterManager _clusterManager;
    private readonly TimeSpan _clusterTimeout = TimeSpan.FromSeconds(3);
    private readonly ConcurrentDictionary<uint, INinePFileSystem> _fids = new();
    private readonly ConcurrentDictionary<uint, SecureString> _authFids = new();

    public NinePFSDispatcher(ILogger<NinePFSDispatcher> logger, IEnumerable<IProtocolBackend> backends, IClusterManager clusterManager)
    {
        _logger = logger;
        _backends = backends;
        _clusterManager = clusterManager;
    }

    public async Task<object> DispatchAsync(NinePMessage message, bool dotu)
    {
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
                    var fs = backend.GetFileSystem(credentials);
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
                if (_authFids.TryRemove(t.Fid, out var secure))
                {
                    secure.Dispose();
                }
                if (_fids.TryRemove(t.Fid, out var fs))
                {
                    return await fs.ClunkAsync(t);
                }
                return new Rclunk(t.Tag);
            }

            if (message.IsMsgTstat)
            {
                var t = ((NinePMessage.MsgTstat)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) throw new NinePProtocolException("Unknown FID");
                return await fs.StatAsync(t);
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
            return new Messages.Rerror(tag, ex.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during dispatch");
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
        if (message.IsMsgTflush) return ((NinePMessage.MsgTflush)message).Item.Tag;
        return 0;
    }
}
