using System;
using System.Threading.Tasks;
using Akka.Actor;
using NinePSharp.Messages;
using NinePSharp.Constants;
using NinePSharp.Interfaces;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Cluster.Actors;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Cluster;

public class RemoteFileSystem : INinePFileSystem
{
    private readonly IActorRef _sessionActor;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public bool DotU { get; set; }

    public RemoteFileSystem(IActorRef sessionActor)
    {
        _sessionActor = sessionActor;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var dto = new TWalkDto(twalk);
        var response = await _sessionActor.Ask(dto, _timeout);
        
        if (response is RWalkDto r) return new Rwalk(r.Tag, r.Wqid);
        if (response is RErrorDto e) throw new NinePProtocolException(e.Ename);
        throw new Exception("Unexpected response from remote actor");
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        var dto = new TReadDto(tread);
        var response = await _sessionActor.Ask(dto, _timeout);

        if (response is RReadDto r) return new Rread(r.Tag, r.Data);
        if (response is RErrorDto e) throw new NinePProtocolException(e.Ename);
        throw new Exception("Unexpected response from remote actor");
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite)
    {
        var dto = new TWriteDto(twrite);
        var response = await _sessionActor.Ask(dto, _timeout);

        if (response is RWriteDto r) return new Rwrite(r.Tag, r.Count);
        if (response is RErrorDto e) throw new NinePProtocolException(e.Ename);
        throw new Exception("Unexpected response from remote actor");
    }

    public async Task<Ropen> OpenAsync(Topen topen)
    {
        var dto = new TOpenDto(topen);
        var response = await _sessionActor.Ask(dto, _timeout);

        if (response is ROpenDto r) return new Ropen(r.Tag, r.Qid, r.Iounit);
        if (response is RErrorDto e) throw new NinePProtocolException(e.Ename);
        throw new Exception("Unexpected response from remote actor");
    }

    public async Task<Rclunk> ClunkAsync(Tclunk tclunk)
    {
        var dto = new TClunkDto(tclunk);
        var response = await _sessionActor.Ask(dto, _timeout);

        if (response is RClunkDto r) return new Rclunk(r.Tag);
        if (response is RErrorDto e) throw new NinePProtocolException(e.Ename);
        throw new Exception("Unexpected response from remote actor");
    }

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var dto = new TStatDto(tstat);
        var response = await _sessionActor.Ask(dto, _timeout);

        if (response is RStatDto r)
        {
            if (r.StatBytes == null || r.StatBytes.Length == 0)
            {
                throw new NinePProtocolException("Remote stat payload was empty.");
            }

            int offset = 0;
            var stat = new Stat(r.StatBytes, ref offset, r.DotU);
            return new Rstat(r.Tag, stat);
        }
        if (response is RErrorDto e) throw new NinePProtocolException(e.Ename);
        throw new Exception("Unexpected response from remote actor");
    }

    public async Task<Rwstat> WstatAsync(Twstat twstat)
    {
        var dto = new TWstatDto(twstat);
        var response = await _sessionActor.Ask(dto, _timeout);

        if (response is RWstatDto r) return new Rwstat(r.Tag);
        if (response is RErrorDto e) throw new NinePProtocolException(e.Ename);
        throw new Exception("Unexpected response from remote actor");
    }

    public async Task<Rremove> RemoveAsync(Tremove tremove)
    {
        var dto = new TRemoveDto(tremove);
        var response = await _sessionActor.Ask(dto, _timeout);

        if (response is RRemoveDto r) return new Rremove(r.Tag);
        if (response is RErrorDto e) throw new NinePProtocolException(e.Ename);
        throw new Exception("Unexpected response from remote actor");
    }

    public async Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        var dto = new TGetAttrDto(tgetattr);
        var response = await _sessionActor.Ask(dto, _timeout);
        
        if (response is RGetAttrDto r)
        {
            return new NinePSharp.Messages.Rgetattr(
                r.Tag, r.Valid, r.Qid, r.Mode, r.Uid, r.Gid, 
                r.Nlink, r.Rdev, r.DataSize, r.BlkSize, r.Blocks, 
                r.AtimeSec, r.AtimeNsec, r.MtimeSec, r.MtimeNsec, 
                r.CtimeSec, r.CtimeNsec, r.BtimeSec, r.BtimeNsec, 
                r.Gen, r.DataVersion);
        }
        if (response is RErrorDto e) throw new NinePProtocolException(e.Ename);
        throw new Exception("Unexpected response from remote actor");
    }

    public async Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr)
    {
        var dto = new TSetAttrDto(tsetattr);
        var response = await _sessionActor.Ask(dto, _timeout);

        if (response is RSetAttrDto r) return new Rsetattr(r.Tag);
        if (response is RErrorDto e) throw new NinePProtocolException(e.Ename);
        throw new Exception("Unexpected response from remote actor");
    }

    public INinePFileSystem Clone()
    {
        // Synchronously blocking on async Ask is not ideal in a Clone() method usually expected to be fast,
        // but INinePFileSystem.Clone() signature is synchronous.
        // We must spawn a new remote actor.
        var response = _sessionActor.Ask(new SpawnClone(), _timeout).Result;
        
        if (response is CloneSpawned c) return new RemoteFileSystem(c.Actor);
        throw new Exception("Failed to clone remote filesystem session");
    }
}
