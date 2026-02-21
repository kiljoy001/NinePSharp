using Microsoft.Extensions.Logging;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NinePSharp.Server;

public class NinePFSDispatcher
{
    private readonly ILogger<NinePFSDispatcher> _logger;
    private readonly IEnumerable<IProtocolBackend> _backends;
    private readonly ConcurrentDictionary<uint, INinePFileSystem> _fids = new();

    public NinePFSDispatcher(ILogger<NinePFSDispatcher> logger, IEnumerable<IProtocolBackend> backends)
    {
        _logger = logger;
        _backends = backends;
    }

    public async Task<object> DispatchAsync(NinePMessage message)
    {
        try
        {
            if (message.IsMsgTversion)
            {
                var t = ((NinePMessage.MsgTversion)message).Item;
                return new Rversion(t.Tag, t.MSize, t.Version);
            }

            if (message.IsMsgTattach)
            {
                var t = ((NinePMessage.MsgTattach)message).Item;
                
                // Use aname to select backend. If empty, default to first (legacy/root behavior)
                var backend = string.IsNullOrEmpty(t.Aname) 
                    ? _backends.FirstOrDefault() 
                    : _backends.FirstOrDefault(b => b.MountPath == t.Aname || b.MountPath == "/" + t.Aname || b.Name == t.Aname);

                if (backend == null) return new Messages.Rerror(t.Tag, $"No backend found for aname '{t.Aname}'");

                var fs = backend.GetFileSystem();
                _fids[t.Fid] = fs;
                
                return new Rattach(t.Tag, new Qid(QidType.QTDIR, 0, 0));
            }

            if (message.IsMsgTwalk)
            {
                var t = ((NinePMessage.MsgTwalk)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) return new Messages.Rerror(t.Tag, "Unknown FID");

                // 9P Walk: If NewFid != Fid, we clone First.
                var targetFs = fs;
                if (t.NewFid != t.Fid)
                {
                    targetFs = fs.Clone();
                }

                var response = await targetFs.WalkAsync(t);
                
                _fids[t.NewFid] = targetFs;
                return response;
            }

            if (message.IsMsgTopen)
            {
                var t = ((NinePMessage.MsgTopen)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) return new Messages.Rerror(t.Tag, "Unknown FID");
                return await fs.OpenAsync(t);
            }

            if (message.IsMsgTread)
            {
                var t = ((NinePMessage.MsgTread)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) return new Messages.Rerror(t.Tag, "Unknown FID");
                return await fs.ReadAsync(t);
            }

            if (message.IsMsgTwrite)
            {
                var t = ((NinePMessage.MsgTwrite)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) return new Messages.Rerror(t.Tag, "Unknown FID");
                return await fs.WriteAsync(t);
            }

            if (message.IsMsgTclunk)
            {
                var t = ((NinePMessage.MsgTclunk)message).Item;
                if (_fids.TryRemove(t.Fid, out var fs))
                {
                    return await fs.ClunkAsync(t);
                }
                return new Rclunk(t.Tag);
            }

            if (message.IsMsgTstat)
            {
                var t = ((NinePMessage.MsgTstat)message).Item;
                if (!_fids.TryGetValue(t.Fid, out var fs)) return new Messages.Rerror(t.Tag, "Unknown FID");
                return await fs.StatAsync(t);
            }

            if (message.IsMsgTflush)
            {
                var t = ((NinePMessage.MsgTflush)message).Item;
                return new Rflush(t.Tag);
            }

            var tag = GetTag(message);
            return new Messages.Rerror(tag, "Not implemented");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching message");
            return new Messages.Rerror(0, ex.Message);
        }
    }

    private ushort GetTag(NinePMessage message)
    {
        if (message.IsMsgTversion) return ((NinePMessage.MsgTversion)message).Item.Tag;
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
