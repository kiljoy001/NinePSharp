namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Rmknod =
    { Size: uint32
      Tag: uint16
      Qid: Qid }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Rmknod
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Rmknod span &offset
            writeQid this.Qid span &offset

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let qid = readQid data &offset
        { Size = size; Tag = tag; Qid = qid }


