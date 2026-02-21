namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Rxattrwalk =
    { Size: uint32
      Tag: uint16
      XattrSize: uint64 }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Rxattrwalk
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Rxattrwalk span &offset
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.XattrSize)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let xattrSize = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        { Size = size; Tag = tag; XattrSize = xattrSize }

