module NinePSharp.Parser.Tests.ParserTests

open Xunit
open FsCheck
open FsCheck.Xunit

open NinePSharp.Messages
open NinePSharp.Constants
open NinePSharp.Parser

[<Fact>]
let ``Parser successfully parses valid Tversion binary vector`` () =
    let tversionBytes = 
        [| 0x13uy; 0x00uy; 0x00uy; 0x00uy
           0x64uy
           0xFFuy; 0xFFuy
           0x00uy; 0x20uy; 0x00uy; 0x00uy
           0x06uy; 0x00uy
           0x39uy; 0x50uy; 0x32uy; 0x30uy; 0x30uy; 0x30uy |]

    match NinePParser.parse false tversionBytes with
    | Ok (MsgTversion t) -> 
        Assert.Equal(8192u, t.MSize)
        Assert.Equal("9P2000", t.Version)
    | _ -> Assert.Fail("Expected Ok(MsgTversion)")

[<Fact>]
let ``Parser parses Tauth 9P2000.u extension with numeric UID`` () =
    // size (4): 23
    // type (1): 102 (Tauth)
    // tag (2): 0x0001
    // afid (4): 10
    // uname len (2): 4 ("root")
    // uname: 'r', 'o', 'o', 't'
    // aname len (2): 0
    // n_uname (4): 0 (Root UID, 9P2000.u extension)
    let tauthUBytes =
        [| 0x17uy; 0x00uy; 0x00uy; 0x00uy
           0x66uy
           0x01uy; 0x00uy
           0x0Auy; 0x00uy; 0x00uy; 0x00uy
           0x04uy; 0x00uy; 0x72uy; 0x6Fuy; 0x6Fuy; 0x74uy
           0x00uy; 0x00uy
           0x00uy; 0x00uy; 0x00uy; 0x00uy |]

    match NinePParser.parse true tauthUBytes with
    | Ok (MsgTauth t) ->
        Assert.Equal(10u, t.Afid)
        Assert.Equal("root", t.Uname)
        Assert.Equal("", t.Aname)
        Assert.True(t.NUname.HasValue) // Verified 9P2000.u numeric mapping
        Assert.Equal(0u, t.NUname.Value)
    | _ -> Assert.Fail("Expected Ok(MsgTauth) with 9u flag")

[<Fact>]
let ``Parser parses Tauth baseline 9P2000 without numeric UID`` () =
    // size (4): 19 (0x13)
    // type (1): 102 (Tauth)
    // tag (2): 0x0001
    // afid (4): 10
    // uname len (2): 4 ("root")
    // uname: 'r', 'o', 'o', 't'
    // aname len (2): 0
    let tauthBytes =
        [| 0x13uy; 0x00uy; 0x00uy; 0x00uy // Size 19
           0x66uy
           0x01uy; 0x00uy
           0x0Auy; 0x00uy; 0x00uy; 0x00uy
           0x04uy; 0x00uy; 0x72uy; 0x6Fuy; 0x6Fuy; 0x74uy
           0x00uy; 0x00uy |]

    match NinePParser.parse false tauthBytes with
    | Ok (MsgTauth t) ->
        Assert.Equal(10u, t.Afid)
        Assert.False(t.NUname.HasValue) // Should be none
    | Error msg -> Assert.Fail(sprintf "Expected Ok(MsgTauth) without 9u flag, got Error: %s" msg)
    | _ -> Assert.Fail("Expected Ok(MsgTauth) without 9u flag, got other message")

[<Fact>]
let ``Parser parses Rlerror 9P2000.L extension`` () =
    // size (4): 11
    // type (1): 7 (Rlerror)
    // tag (2): 0x0001
    // ecode (4): 2 (ENOENT)
    let rlerrorBytes =
        [| 0x0Buy; 0x00uy; 0x00uy; 0x00uy
           0x07uy
           0x01uy; 0x00uy
           0x02uy; 0x00uy; 0x00uy; 0x00uy |]

    match NinePParser.parse false rlerrorBytes with
    | Ok (MsgRlerror r) ->
        Assert.Equal(2u, r.Ecode)
    | _ -> Assert.Fail("Expected Ok(MsgRlerror)")

[<Property>]
let ``Parser never throws unhandled exceptions on arbitrary byte payloads`` (payload: byte[], is9u: bool) =
    let safePayload = if isNull payload then [||] else payload
    try
        match NinePParser.parse is9u safePayload with
        | Ok _ | Error _ -> true
    with
    | _ -> false

[<Property>]
let ``Parser gracefully returns Error when receiving truncated valid payload`` (amount: int, is9u: bool) =
    let validTversion = 
        [| 0x13uy; 0x00uy; 0x00uy; 0x00uy
           0x64uy
           0xFFuy; 0xFFuy
           0x00uy; 0x20uy; 0x00uy; 0x00uy
           0x06uy; 0x00uy
           0x39uy; 0x50uy; 0x32uy; 0x30uy; 0x30uy; 0x30uy |]

    let safeAmount = if amount < 0 then -amount else amount
    let len = safeAmount % validTversion.Length
    let truncated = Array.take len validTversion
    
    match NinePParser.parse is9u truncated with
    | Error _ -> true
    | Ok _ -> false
