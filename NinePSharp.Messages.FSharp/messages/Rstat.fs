namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Rstat =
    { Size: uint32
      Tag: uint16
      Stat: Stat }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Rstat
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Rstat span &offset
            this.Stat.WriteTo(span, &offset)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let stat = Stat(data, &offset)
        { Size = size; Tag = tag; Stat = stat }

