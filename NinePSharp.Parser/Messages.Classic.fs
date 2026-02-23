namespace NinePSharp.Parser

open NinePSharp.Messages
open NinePSharp.Constants
open System

module Classic =
    let parse (msgType: byte) (data: ReadOnlyMemory<byte>) (dialect: NinePDialect) =
        let span = data.Span
        let is9u = Dialect.is9u dialect
        match msgType with
        | t when t = byte MessageTypes.Tversion -> Ok (MsgTversion(new Tversion(span)))
        | t when t = byte MessageTypes.Rversion -> Ok (MsgRversion(new Rversion(span)))
        | t when t = byte MessageTypes.Tauth -> Ok (MsgTauth(new Tauth(span, is9u)))
        | t when t = byte MessageTypes.Rauth -> Ok (MsgRauth(new Rauth(span)))
        | t when t = byte MessageTypes.Tattach -> Ok (MsgTattach(new Tattach(span, is9u)))
        | t when t = byte MessageTypes.Rattach -> Ok (MsgRattach(new Rattach(span)))
        | t when t = byte MessageTypes.Rerror -> Ok (MsgRerror(new Rerror(span, is9u)))
        | t when t = byte MessageTypes.Topen -> Ok (MsgTopen(new Topen(span)))
        | t when t = byte MessageTypes.Ropen -> Ok (MsgRopen(new Ropen(span)))
        | t when t = byte MessageTypes.Tcreate -> Ok (MsgTcreate(new Tcreate(span)))
        | t when t = byte MessageTypes.Rcreate -> Ok (MsgRcreate(new Rcreate(span)))
        | t when t = byte MessageTypes.Tread -> Ok (MsgTread(new Tread(span)))
        | t when t = byte MessageTypes.Rread -> Ok (MsgRread(new Rread(data)))
        | t when t = byte MessageTypes.Twrite -> Ok (MsgTwrite(new Twrite(data)))
        | t when t = byte MessageTypes.Rwrite -> Ok (MsgRwrite(new Rwrite(span)))
        | t when t = byte MessageTypes.Tclunk -> Ok (MsgTclunk(new Tclunk(span)))
        | t when t = byte MessageTypes.Rclunk -> Ok (MsgRclunk(new Rclunk(span)))
        | t when t = byte MessageTypes.Tremove -> Ok (MsgTremove(new Tremove(span)))
        | t when t = byte MessageTypes.Rremove -> Ok (MsgRremove(new Rremove(span)))
        | t when t = byte MessageTypes.Tstat -> Ok (MsgTstat(new Tstat(span)))
        | t when t = byte MessageTypes.Rstat -> Ok (MsgRstat(new Rstat(span)))
        | t when t = byte MessageTypes.Twstat -> Ok (MsgTwstat(new Twstat(span)))
        | t when t = byte MessageTypes.Rwstat -> Ok (MsgRwstat(new Rwstat(span)))
        | t when t = byte MessageTypes.Twalk -> Ok (MsgTwalk(new Twalk(span)))
        | t when t = byte MessageTypes.Rwalk -> Ok (MsgRwalk(new Rwalk(span)))
        | t when t = byte MessageTypes.Tflush -> Ok (MsgTflush(new Tflush(span)))
        | t when t = byte MessageTypes.Rflush -> Ok (MsgRflush(new Rflush(span)))
        | _ -> Error (UnknownMessageType msgType)
