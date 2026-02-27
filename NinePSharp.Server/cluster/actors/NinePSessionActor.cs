using System;
using System.Threading.Tasks;
using Akka.Actor;
using NinePSharp.Messages;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Cluster.Actors;

public class NinePSessionActor : ReceiveActor
{
    private readonly INinePFileSystem _fs;

    public NinePSessionActor(INinePFileSystem fs)
    {
        _fs = fs;

        ReceiveAsync<TWalkDto>(async msg => {
            var twalk = new Twalk(msg.Tag, msg.Fid, msg.NewFid, msg.Wname);
            try {
                var res = await _fs.WalkAsync(twalk);
                Sender.Tell(new RWalkDto(res));
            } catch (Exception ex) { Sender.Tell(new RErrorDto { Tag = msg.Tag, Ename = ex.Message }); }
        });

        ReceiveAsync<TReadDto>(async msg => {
            var tread = new Tread(msg.Tag, msg.Fid, msg.Offset, msg.Count);
            try {
                var res = await _fs.ReadAsync(tread);
                Sender.Tell(new RReadDto(res));
            } catch (Exception ex) { Sender.Tell(new RErrorDto { Tag = msg.Tag, Ename = ex.Message }); }
        });

        ReceiveAsync<TWriteDto>(async msg => {
            var twrite = new Twrite(msg.Tag, msg.Fid, msg.Offset, msg.Data);
            try {
                var res = await _fs.WriteAsync(twrite);
                Sender.Tell(new RWriteDto(res));
            } catch (Exception ex) { Sender.Tell(new RErrorDto { Tag = msg.Tag, Ename = ex.Message }); }
        });

        ReceiveAsync<TOpenDto>(async msg => {
            var topen = new Topen(msg.Tag, msg.Fid, msg.Mode);
            try {
                var res = await _fs.OpenAsync(topen);
                Sender.Tell(new ROpenDto(res));
            } catch (Exception ex) { Sender.Tell(new RErrorDto { Tag = msg.Tag, Ename = ex.Message }); }
        });

        ReceiveAsync<TClunkDto>(async msg => {
            var tclunk = new Tclunk(msg.Tag, msg.Fid);
            try {
                var res = await _fs.ClunkAsync(tclunk);
                Sender.Tell(new RClunkDto(res));
                // Clunk often implies end of session for that fid, 
                // but since we are wrapping an FS instance which might handle multiple fids if we shared it,
                // or just one path state... 
                // In our architecture, INinePFileSystem is usually 1:1 with a logical path state.
                // If it's a clone, it's a new actor.
            } catch (Exception ex) { Sender.Tell(new RErrorDto { Tag = msg.Tag, Ename = ex.Message }); }
        });

        ReceiveAsync<TStatDto>(async msg => {
            var tstat = new Tstat(msg.Tag, msg.Fid);
            try {
                var res = await _fs.StatAsync(tstat);
                Sender.Tell(new RStatDto(res));
            } catch (Exception ex) { Sender.Tell(new RErrorDto { Tag = msg.Tag, Ename = ex.Message }); }
        });

        ReceiveAsync<TGetAttrDto>(async msg => {
            var tgetattr = new Tgetattr(msg.Tag, msg.Fid, msg.RequestMask);
            try {
                var res = await _fs.GetAttrAsync(tgetattr);
                Sender.Tell(new RGetAttrDto(res));
            } catch (Exception ex) { Sender.Tell(new RErrorDto { Tag = msg.Tag, Ename = ex.Message }); }
        });

        ReceiveAsync<TRemoveDto>(async msg => {
            var tremove = new Tremove(msg.Tag, msg.Fid);
            try {
                var res = await _fs.RemoveAsync(tremove);
                Sender.Tell(new RRemoveDto(res));
            } catch (Exception ex) { Sender.Tell(new RErrorDto { Tag = msg.Tag, Ename = ex.Message }); }
        });

        ReceiveAsync<TWstatDto>(async msg => {
            int offset = 0;
            var stat = new Stat(msg.StatBytes, ref offset, msg.Dialect);
            var twstat = new Twstat(msg.Tag, msg.Fid, stat);
            try {
                var res = await _fs.WstatAsync(twstat);
                Sender.Tell(new RWstatDto(res));
            } catch (Exception ex) { Sender.Tell(new RErrorDto { Tag = msg.Tag, Ename = ex.Message }); }
        });

        ReceiveAsync<TSetAttrDto>(async msg => {
            var tsetattr = new Tsetattr(0, msg.Tag, msg.Fid, msg.Valid, msg.Mode, msg.Uid, msg.Gid, msg.FileSize, msg.AtimeSec, msg.AtimeNsec, msg.MtimeSec, msg.MtimeNsec);
            try {
                var res = await _fs.SetAttrAsync(tsetattr);
                Sender.Tell(new RSetAttrDto(res));
            } catch (Exception ex) { Sender.Tell(new RErrorDto { Tag = msg.Tag, Ename = ex.Message }); }
        });
        
        Receive<SpawnClone>(msg => {
            // Create a new actor for the clone
            var cloneFs = _fs.Clone();
            var cloneActor = Context.ActorOf(Props.Create(() => new NinePSessionActor(cloneFs)));
            Sender.Tell(new CloneSpawned(cloneActor));
        });
    }
}

public class SpawnClone {}
public class CloneSpawned
{
    public IActorRef Actor { get; }
    public CloneSpawned(IActorRef actor) => Actor = actor;
}
