namespace NinePSharp.Parser

open NinePSharp.Messages
open NinePSharp.Constants
open System

type NinePMessage =
    | MsgTversion of Tversion
    | MsgRversion of Rversion
    | MsgTauth of Tauth
    | MsgRauth of Rauth
    | MsgTattach of Tattach
    | MsgRattach of Rattach
    | MsgRerror of Rerror
    | MsgRlerror of Rlerror
    | MsgTopen of Topen
    | MsgRopen of Ropen
    | MsgTcreate of Tcreate
    | MsgRcreate of Rcreate
    | MsgTread of Tread
    | MsgRread of Rread
    | MsgTwrite of Twrite
    | MsgRwrite of Rwrite
    | MsgTclunk of Tclunk
    | MsgRclunk of Rclunk
    | MsgTremove of Tremove
    | MsgRremove of Rremove
    | MsgTstat of Tstat
    | MsgRstat of Rstat
    | MsgTwstat of Twstat
    | MsgRwstat of Rwstat
    | MsgTwalk of Twalk
    | MsgRwalk of Rwalk
    | MsgTflush of Tflush
    | MsgRflush of Rflush
    | MsgTstatfs of Tstatfs
    | MsgRstatfs of Rstatfs
    | MsgTlopen of Tlopen
    | MsgRlopen of Rlopen
    | MsgTlcreate of Tlcreate
    | MsgRlcreate of Rlcreate
    | MsgTsymlink of Tsymlink
    | MsgRsymlink of Rsymlink
    | MsgTmknod of Tmknod
    | MsgRmknod of Rmknod
    | MsgTrename of Trename
    | MsgRrename of Rrename
    | MsgTreadlink of Treadlink
    | MsgRreadlink of Rreadlink
    | MsgTgetattr of Tgetattr
    | MsgRgetattr of Rgetattr
    | MsgTsetattr of Tsetattr
    | MsgRsetattr of Rsetattr
    | MsgTxattrwalk of Txattrwalk
    | MsgRxattrwalk of Rxattrwalk
    | MsgTxattrcreate of Txattrcreate
    | MsgRxattrcreate of Rxattrcreate
    | MsgTreaddir of Treaddir
    | MsgRreaddir of Rreaddir
    | MsgTfsync of Tfsync
    | MsgRfsync of Rfsync
    | MsgTlock of Tlock
    | MsgRlock of Rlock
    | MsgTgetlock of Tgetlock
    | MsgRgetlock of Rgetlock
    | MsgTlink of Tlink
    | MsgRlink of Rlink
    | MsgTmkdir of Tmkdir
    | MsgRmkdir of Rmkdir
    | MsgTrenameat of Trenameat
    | MsgRrenameat of Rrenameat
    | MsgTunlinkat of Tunlinkat
    | MsgRunlinkat of Runlinkat

module NinePParser =
    
    // Performance optimized parse taking ReadOnlyMemory to allow zero-copy into messages
    let parse (is9u: bool) (data: ReadOnlyMemory<byte>) : Result<NinePMessage, string> =
        if data.Length < int NinePConstants.HeaderSize then
            Error "Message too short"
        else
            let span = data.Span
            try
                let size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4))
                
                if size > uint32 data.Length then
                    Error "Payload truncated: reported size exceeds buffer"
                else
                    let msgType = span.[4]
                    // Create a slice representing the full message for constructors that expect it
                    let msgData = data.Slice(0, int size)
                    
                    match msgType with
                    | t when t = byte MessageTypes.Tversion -> Ok (MsgTversion(new Tversion(span)))
                    | t when t = byte MessageTypes.Rversion -> Ok (MsgRversion(new Rversion(span)))
                    | t when t = byte MessageTypes.Tauth -> Ok (MsgTauth(new Tauth(span, is9u)))
                    | t when t = byte MessageTypes.Rauth -> Ok (MsgRauth(new Rauth(span)))
                    | t when t = byte MessageTypes.Tattach -> Ok (MsgTattach(new Tattach(span, is9u)))
                    | t when t = byte MessageTypes.Rattach -> Ok (MsgRattach(new Rattach(span)))
                    | t when t = byte MessageTypes.Rerror -> Ok (MsgRerror(new Rerror(span, is9u)))
                    | t when t = byte MessageTypes.Rlerror -> Ok (MsgRlerror(new Rlerror(span)))
                    | t when t = byte MessageTypes.Topen -> Ok (MsgTopen(new Topen(span)))
                    | t when t = byte MessageTypes.Ropen -> Ok (MsgRopen(new Ropen(span)))
                    | t when t = byte MessageTypes.Tcreate -> Ok (MsgTcreate(new Tcreate(span)))
                    | t when t = byte MessageTypes.Rcreate -> Ok (MsgRcreate(new Rcreate(span)))
                    | t when t = byte MessageTypes.Tread -> Ok (MsgTread(new Tread(span)))
                    | t when t = byte MessageTypes.Rread -> Ok (MsgRread(new Rread(msgData)))
                    | t when t = byte MessageTypes.Twrite -> Ok (MsgTwrite(new Twrite(msgData)))
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
                    | _ -> Error (sprintf "Unknown message type: %d" msgType)
            with
            | :? System.IndexOutOfRangeException -> Error "Malformed message bounds"
            | ex -> Error (sprintf "Parser exception: %s" ex.Message)
