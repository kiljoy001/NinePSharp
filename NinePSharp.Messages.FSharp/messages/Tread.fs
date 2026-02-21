namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Tread =
    { Size: uint32
      Tag: uint16
      Fid: uint32
      Offset: uint64
      Count: uint32 }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Tread
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Tread span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Fid); offset <- offset + 4
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Offset); offset <- offset + 8
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Count)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let foffset = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        { Size = size; Tag = tag; Fid = fid; Offset = foffset; Count = count }

