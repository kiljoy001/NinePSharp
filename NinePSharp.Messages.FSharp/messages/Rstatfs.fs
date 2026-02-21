namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Rstatfs =
    { Size: uint32
      Tag: uint16
      FsType: uint32
      BSize: uint32
      Blocks: uint64
      BFree: uint64
      BAvail: uint64
      Files: uint64
      FFree: uint64
      FsId: uint64
      NameLen: uint32 }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Rstatsfs
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Rstatsfs span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.FsType); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.BSize); offset <- offset + 4
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Blocks); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.BFree); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.BAvail); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Files); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.FFree); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.FsId); offset <- offset + 8
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.NameLen)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fsType = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let bSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let blocks = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let bFree = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let bAvail = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let files = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let fFree = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let fsId = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let nameLen = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        { Size = size; Tag = tag; FsType = fsType; BSize = bSize; Blocks = blocks; BFree = bFree; BAvail = bAvail; Files = files; FFree = fFree; FsId = fsId; NameLen = nameLen }

