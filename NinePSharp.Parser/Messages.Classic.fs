namespace NinePSharp.Parser

open NinePSharp.Messages
open NinePSharp.Constants
open System

module Classic =
    let private createParsers is9u =
        dict [
            byte MessageTypes.Tversion, fun (data: ReadOnlyMemory<byte>) -> MsgTversion(new Tversion(data.Span))
            byte MessageTypes.Rversion, fun data -> MsgRversion(new Rversion(data.Span))
            byte MessageTypes.Tauth, fun data -> MsgTauth(new Tauth(data.Span, is9u))
            byte MessageTypes.Rauth, fun data -> MsgRauth(new Rauth(data.Span))
            byte MessageTypes.Tattach, fun data -> MsgTattach(new Tattach(data.Span, is9u))
            byte MessageTypes.Rattach, fun data -> MsgRattach(new Rattach(data.Span))
            byte MessageTypes.Rerror, fun data -> MsgRerror(new Rerror(data.Span, is9u))
            byte MessageTypes.Topen, fun data -> MsgTopen(new Topen(data.Span))
            byte MessageTypes.Ropen, fun data -> MsgRopen(new Ropen(data.Span))
            byte MessageTypes.Tcreate, fun data -> MsgTcreate(new Tcreate(data.Span))
            byte MessageTypes.Rcreate, fun data -> MsgRcreate(new Rcreate(data.Span))
            byte MessageTypes.Tread, fun data -> MsgTread(new Tread(data.Span))
            byte MessageTypes.Rread, fun data -> MsgRread(new Rread(data))
            byte MessageTypes.Twrite, fun data -> MsgTwrite(new Twrite(data))
            byte MessageTypes.Rwrite, fun data -> MsgRwrite(new Rwrite(data.Span))
            byte MessageTypes.Tclunk, fun data -> MsgTclunk(new Tclunk(data.Span))
            byte MessageTypes.Rclunk, fun data -> MsgRclunk(new Rclunk(data.Span))
            byte MessageTypes.Tremove, fun data -> MsgTremove(new Tremove(data.Span))
            byte MessageTypes.Rremove, fun data -> MsgRremove(new Rremove(data.Span))
            byte MessageTypes.Tstat, fun data -> MsgTstat(new Tstat(data.Span))
            byte MessageTypes.Rstat, fun data -> MsgRstat(new Rstat(data.Span))
            byte MessageTypes.Twstat, fun data -> MsgTwstat(new Twstat(data.Span))
            byte MessageTypes.Rwstat, fun data -> MsgRwstat(new Rwstat(data.Span))
            byte MessageTypes.Twalk, fun data -> MsgTwalk(new Twalk(data.Span))
            byte MessageTypes.Rwalk, fun data -> MsgRwalk(new Rwalk(data.Span))
            byte MessageTypes.Tflush, fun data -> MsgTflush(new Tflush(data.Span))
            byte MessageTypes.Rflush, fun data -> MsgRflush(new Rflush(data.Span))
        ]

    let private classicParsers = createParsers false
    let private nineP2000uParsers = createParsers true

    let parse (msgType: byte) (data: ReadOnlyMemory<byte>) (dialect: NinePDialect) =
        let parsers = if Dialect.is9u dialect then nineP2000uParsers else classicParsers
        match parsers.TryGetValue msgType with
        | true, parser -> Ok (parser data)
        | false, _ -> Error (UnknownMessageType msgType)
