namespace NinePSharp.Parser

open NinePSharp.Messages
open NinePSharp.Constants
open System

module Linux =
    let private parsers =
        dict [
            byte MessageTypes.Rlerror, fun (data: ReadOnlyMemory<byte>) -> MsgRlerror(new Rlerror(data.Span))
            byte MessageTypes.Tstatfs, fun data -> MsgTstatfs(new Tstatfs(data.Span))
            byte MessageTypes.Rstatsfs, fun data -> MsgRstatfs(new Rstatfs(data.Span))
            byte MessageTypes.Tlopen, fun data -> MsgTlopen(new Tlopen(data.Span))
            byte MessageTypes.RLopen, fun data -> MsgRlopen(new Rlopen(data.Span))
            byte MessageTypes.Tlcreate, fun data -> MsgTlcreate(new Tlcreate(data.Span))
            byte MessageTypes.Rlcreate, fun data -> MsgRlcreate(new Rlcreate(data.Span))
            byte MessageTypes.Tsymlink, fun data -> MsgTsymlink(new Tsymlink(data.Span))
            byte MessageTypes.Rsymlink, fun data -> MsgRsymlink(new Rsymlink(data.Span))
            byte MessageTypes.Tmknod, fun data -> MsgTmknod(new Tmknod(data.Span))
            byte MessageTypes.Rmknod, fun data -> MsgRmknod(new Rmknod(data.Span))
            byte MessageTypes.Trename, fun data -> MsgTrename(new Trename(data.Span))
            byte MessageTypes.Rrename, fun data -> MsgRrename(new Rrename(data.Span))
            byte MessageTypes.Treadlink, fun data -> MsgTreadlink(new Treadlink(data.Span))
            byte MessageTypes.Rreadlink, fun data -> MsgRreadlink(new Rreadlink(data.Span))
            byte MessageTypes.Tgetattr, fun data -> MsgTgetattr(new Tgetattr(data.Span))
            byte MessageTypes.Rgetattr, fun data -> MsgRgetattr(new Rgetattr(data.Span))
            byte MessageTypes.Tsetattr, fun data -> MsgTsetattr(new Tsetattr(data.Span))
            byte MessageTypes.Rsetattr, fun data -> MsgRsetattr(new Rsetattr(data.Span))
            byte MessageTypes.Txattrwalk, fun data -> MsgTxattrwalk(new Txattrwalk(data.Span))
            byte MessageTypes.Rxattrwalk, fun data -> MsgRxattrwalk(new Rxattrwalk(data.Span))
            byte MessageTypes.Txattrcreate, fun data -> MsgTxattrcreate(new Txattrcreate(data.Span))
            byte MessageTypes.Rxattrcreate, fun data -> MsgRxattrcreate(new Rxattrcreate(data.Span))
            byte MessageTypes.Treaddir, fun data -> MsgTreaddir(new Treaddir(data.Span))
            byte MessageTypes.Rreaddir, fun data -> MsgRreaddir(new Rreaddir(data.Span))
            byte MessageTypes.Tfsync, fun data -> MsgTfsync(new Tfsync(data.Span))
            byte MessageTypes.Rfsync, fun data -> MsgRfsync(new Rfsync(data.Span))
            byte MessageTypes.Tlock, fun data -> MsgTlock(new Tlock(data.Span))
            byte MessageTypes.Rlock, fun data -> MsgRlock(new Rlock(data.Span))
            byte MessageTypes.Tgetlock, fun data -> MsgTgetlock(new Tgetlock(data.Span))
            byte MessageTypes.Rgetlock, fun data -> MsgRgetlock(new Rgetlock(data.Span))
            byte MessageTypes.Tlink, fun data -> MsgTlink(new Tlink(data.Span))
            byte MessageTypes.Rlink, fun data -> MsgRlink(new Rlink(data.Span))
            byte MessageTypes.Tmkdir, fun data -> MsgTmkdir(new Tmkdir(data.Span))
            byte MessageTypes.Rmkdir, fun data -> MsgRmkdir(new Rmkdir(data.Span))
            byte MessageTypes.Trenameat, fun data -> MsgTrenameat(new Trenameat(data.Span))
            byte MessageTypes.Rrenameat, fun data -> MsgRrenameat(new Rrenameat(data.Span))
            byte MessageTypes.Tunlinkat, fun data -> MsgTunlinkat(new Tunlinkat(data.Span))
            byte MessageTypes.Runlinkat, fun data -> MsgRunlinkat(new Runlinkat(data.Span))
        ]

    let parse (msgType: byte) (data: ReadOnlyMemory<byte>) =
        match parsers.TryGetValue msgType with
        | true, parser -> Ok (parser data)
        | false, _ -> Error (UnknownMessageType msgType)
