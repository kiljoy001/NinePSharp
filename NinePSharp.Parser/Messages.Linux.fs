namespace NinePSharp.Parser

open NinePSharp.Messages
open NinePSharp.Constants
open System

module Linux =
    let parse (msgType: byte) (data: ReadOnlyMemory<byte>) =
        let span = data.Span
        match msgType with
        | t when t = byte MessageTypes.Rlerror -> Ok (MsgRlerror(new Rlerror(span)))
        | t when t = byte MessageTypes.Tstatfs -> Ok (MsgTstatfs(new Tstatfs(span)))
        | t when t = byte MessageTypes.Rstatsfs -> Ok (MsgRstatfs(new Rstatfs(span)))
        | t when t = byte MessageTypes.Tlopen -> Ok (MsgTlopen(new Tlopen(span)))
        | t when t = byte MessageTypes.RLopen -> Ok (MsgRlopen(new Rlopen(span)))
        | t when t = byte MessageTypes.Tlcreate -> Ok (MsgTlcreate(new Tlcreate(span)))
        | t when t = byte MessageTypes.Rlcreate -> Ok (MsgRlcreate(new Rlcreate(span)))
        | t when t = byte MessageTypes.Tsymlink -> Ok (MsgTsymlink(new Tsymlink(span)))
        | t when t = byte MessageTypes.Rsymlink -> Ok (MsgRsymlink(new Rsymlink(span)))
        | t when t = byte MessageTypes.Tmknod -> Ok (MsgTmknod(new Tmknod(span)))
        | t when t = byte MessageTypes.Rmknod -> Ok (MsgRmknod(new Rmknod(span)))
        | t when t = byte MessageTypes.Trename -> Ok (MsgTrename(new Trename(span)))
        | t when t = byte MessageTypes.Rrename -> Ok (MsgRrename(new Rrename(span)))
        | t when t = byte MessageTypes.Treadlink -> Ok (MsgTreadlink(new Treadlink(span)))
        | t when t = byte MessageTypes.Rreadlink -> Ok (MsgRreadlink(new Rreadlink(span)))
        | t when t = byte MessageTypes.Tgetattr -> Ok (MsgTgetattr(new Tgetattr(span)))
        | t when t = byte MessageTypes.Rgetattr -> Ok (MsgRgetattr(new Rgetattr(span)))
        | t when t = byte MessageTypes.Tsetattr -> Ok (MsgTsetattr(new Tsetattr(span)))
        | t when t = byte MessageTypes.Rsetattr -> Ok (MsgRsetattr(new Rsetattr(span)))
        | t when t = byte MessageTypes.Txattrwalk -> Ok (MsgTxattrwalk(new Txattrwalk(span)))
        | t when t = byte MessageTypes.Rxattrwalk -> Ok (MsgRxattrwalk(new Rxattrwalk(span)))
        | t when t = byte MessageTypes.Txattrcreate -> Ok (MsgTxattrcreate(new Txattrcreate(span)))
        | t when t = byte MessageTypes.Rxattrcreate -> Ok (MsgRxattrcreate(new Rxattrcreate(span)))
        | t when t = byte MessageTypes.Treaddir -> Ok (MsgTreaddir(new Treaddir(span)))
        | t when t = byte MessageTypes.Rreaddir -> Ok (MsgRreaddir(new Rreaddir(span)))
        | t when t = byte MessageTypes.Tfsync -> Ok (MsgTfsync(new Tfsync(span)))
        | t when t = byte MessageTypes.Rfsync -> Ok (MsgRfsync(new Rfsync(span)))
        | t when t = byte MessageTypes.Tlock -> Ok (MsgTlock(new Tlock(span)))
        | t when t = byte MessageTypes.Rlock -> Ok (MsgRlock(new Rlock(span)))
        | t when t = byte MessageTypes.Tgetlock -> Ok (MsgTgetlock(new Tgetlock(span)))
        | t when t = byte MessageTypes.Rgetlock -> Ok (MsgRgetlock(new Rgetlock(span)))
        | t when t = byte MessageTypes.Tlink -> Ok (MsgTlink(new Tlink(span)))
        | t when t = byte MessageTypes.Rlink -> Ok (MsgRlink(new Rlink(span)))
        | t when t = byte MessageTypes.Tmkdir -> Ok (MsgTmkdir(new Tmkdir(span)))
        | t when t = byte MessageTypes.Rmkdir -> Ok (MsgRmkdir(new Rmkdir(span)))
        | t when t = byte MessageTypes.Trenameat -> Ok (MsgTrenameat(new Trenameat(span)))
        | t when t = byte MessageTypes.Rrenameat -> Ok (MsgRrenameat(new Rrenameat(span)))
        | t when t = byte MessageTypes.Tunlinkat -> Ok (MsgTunlinkat(new Tunlinkat(span)))
        | t when t = byte MessageTypes.Runlinkat -> Ok (MsgRunlinkat(new Runlinkat(span)))
        | _ -> Error (UnknownMessageType msgType)
