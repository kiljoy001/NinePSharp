namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Tgetattr =
    { Size: uint32
      Tag: uint16
      Fid: uint32
      RequestMask: uint64 }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Tgetattr
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Tgetattr span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Fid); offset <- offset + 4
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.RequestMask)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let requestMask = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        { Size = size; Tag = tag; Fid = fid; RequestMask = requestMask }

