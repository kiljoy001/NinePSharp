namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Rreadlink =
    { Size: uint32
      Tag: uint16
      Target: string }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Rreadlink
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Rreadlink span &offset
            writeString this.Target span &offset

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let target = readString data &offset
        { Size = size; Tag = tag; Target = target }

