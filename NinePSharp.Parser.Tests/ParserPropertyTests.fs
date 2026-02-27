module NinePSharp.Parser.Tests.Properties

open Xunit
open FsCheck
open FsCheck.Xunit
open NinePSharp.Parser
open NinePSharp.Messages
open NinePSharp.Interfaces
open NinePSharp.Generators
open NinePSharp.Constants
open System

module Serializer =
    let getSerializable (msg: NinePMessage) : ISerializable =
        match msg with
        | MsgTversion m -> m :> ISerializable
        | MsgRversion m -> m :> ISerializable
        | MsgTauth m -> m :> ISerializable
        | MsgRauth m -> m :> ISerializable
        | MsgTattach m -> m :> ISerializable
        | MsgRattach m -> m :> ISerializable
        | MsgRerror m -> m :> ISerializable
        | MsgRlerror m -> m :> ISerializable
        | MsgTopen m -> m :> ISerializable
        | MsgRopen m -> m :> ISerializable
        | MsgTcreate m -> m :> ISerializable
        | MsgRcreate m -> m :> ISerializable
        | MsgTread m -> m :> ISerializable
        | MsgRread m -> m :> ISerializable
        | MsgTwrite m -> m :> ISerializable
        | MsgRwrite m -> m :> ISerializable
        | MsgTclunk m -> m :> ISerializable
        | MsgRclunk m -> m :> ISerializable
        | MsgTremove m -> m :> ISerializable
        | MsgRremove m -> m :> ISerializable
        | MsgTstat m -> m :> ISerializable
        | MsgRstat m -> m :> ISerializable
        | MsgTwstat m -> m :> ISerializable
        | MsgRwstat m -> m :> ISerializable
        | MsgTwalk m -> m :> ISerializable
        | MsgRwalk m -> m :> ISerializable
        | MsgTflush m -> m :> ISerializable
        | MsgRflush m -> m :> ISerializable
        | MsgTstatfs m -> m :> ISerializable
        | MsgRstatfs m -> m :> ISerializable
        | MsgTlopen m -> m :> ISerializable
        | MsgRlopen m -> m :> ISerializable
        | MsgTlcreate m -> m :> ISerializable
        | MsgRlcreate m -> m :> ISerializable
        | MsgTsymlink m -> m :> ISerializable
        | MsgRsymlink m -> m :> ISerializable
        | MsgTmknod m -> m :> ISerializable
        | MsgRmknod m -> m :> ISerializable
        | MsgTrename m -> m :> ISerializable
        | MsgRrename m -> m :> ISerializable
        | MsgTreadlink m -> m :> ISerializable
        | MsgRreadlink m -> m :> ISerializable
        | MsgTgetattr m -> m :> ISerializable
        | MsgRgetattr m -> m :> ISerializable
        | MsgTsetattr m -> m :> ISerializable
        | MsgRsetattr m -> m :> ISerializable
        | MsgTxattrwalk m -> m :> ISerializable
        | MsgRxattrwalk m -> m :> ISerializable
        | MsgTxattrcreate m -> m :> ISerializable
        | MsgRxattrcreate m -> m :> ISerializable
        | MsgTreaddir m -> m :> ISerializable
        | MsgRreaddir m -> m :> ISerializable
        | MsgTfsync m -> m :> ISerializable
        | MsgRfsync m -> m :> ISerializable
        | MsgTlock m -> m :> ISerializable
        | MsgRlock m -> m :> ISerializable
        | MsgTgetlock m -> m :> ISerializable
        | MsgRgetlock m -> m :> ISerializable
        | MsgTlink m -> m :> ISerializable
        | MsgRlink m -> m :> ISerializable
        | MsgTmkdir m -> m :> ISerializable
        | MsgRmkdir m -> m :> ISerializable
        | MsgTrenameat m -> m :> ISerializable
        | MsgRrenameat m -> m :> ISerializable
        | MsgTunlinkat m -> m :> ISerializable
        | MsgRunlinkat m -> m :> ISerializable

    let serialize (msg: NinePMessage) : byte[] =
        let s = getSerializable msg
        let buffer = Array.zeroCreate (int s.Size)
        s.WriteTo(Span<byte>(buffer))
        buffer

module ParserPropertyTests =

    let private isCoreNineP2000Message (msg: NinePMessage) =
        match msg with
        | MsgTversion _
        | MsgRversion _
        | MsgTauth _
        | MsgRauth _
        | MsgTattach _
        | MsgRattach _
        | MsgRerror _
        | MsgTflush _
        | MsgRflush _
        | MsgTwalk _
        | MsgRwalk _
        | MsgTopen _
        | MsgRopen _
        | MsgTcreate _
        | MsgRcreate _
        | MsgTread _
        | MsgRread _
        | MsgTwrite _
        | MsgRwrite _
        | MsgTclunk _
        | MsgRclunk _
        | MsgTremove _
        | MsgRremove _
        | MsgTstat _
        | MsgRstat _
        | MsgTwstat _
        | MsgRwstat _ -> true
        | _ -> false
    
    [<Property(Arbitrary = [| typeof<Generators.NinePArb> |], MaxTest = 1000)>]
    let ``Parser round-trip consistency`` (msg: NinePMessage) =
        if isCoreNineP2000Message msg then
            let data = Serializer.serialize msg
            let result = NinePParser.parse NinePDialect.NineP2000 (ReadOnlyMemory<byte>(data))
            
            match result with
            | Ok parsedMsg -> 
                let originalBytes = Serializer.serialize msg
                let parsedBytes = Serializer.serialize parsedMsg
                Assert.Equal<byte>(originalBytes, parsedBytes)
            | Error err -> 
                Assert.Fail(sprintf "Parse failed for message %A: %s" msg err)
