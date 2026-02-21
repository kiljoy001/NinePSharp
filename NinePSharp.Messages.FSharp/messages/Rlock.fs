namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Rlock =
    { Size: uint32
      Tag: uint16
      Status: byte }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Rlock
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Rlock span &offset
            span.[NinePConstants.HeaderSize] <- this.Status

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let status = data.[NinePConstants.HeaderSize]
        { Size = size; Tag = tag; Status = status }

