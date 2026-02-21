namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Tflush =
    { Size: uint32
      Tag: uint16
      OldTag: uint16 }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Tflush
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Tflush span &offset
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), this.OldTag)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let oldtag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2))
        { Size = size; Tag = tag; OldTag = oldtag }

