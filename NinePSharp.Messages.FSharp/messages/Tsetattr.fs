namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Tsetattr =
    { Size: uint32
      Tag: uint16
      Fid: uint32
      Valid: uint32
      Mode: uint32
      Uid: uint32
      Gid: uint32
      FileSize: uint64
      AtimeSec: uint64
      AtimeNsec: uint64
      MtimeSec: uint64
      MtimeNsec: uint64 }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Tsetattr
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Tsetattr span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Fid); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Valid); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Mode); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Uid); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Gid); offset <- offset + 4
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.FileSize); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.AtimeSec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.AtimeNsec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.MtimeSec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.MtimeNsec)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let valid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let mode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let uid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let gid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let fileSize = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let atimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let atimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let mtimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let mtimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        { Size = size; Tag = tag; Fid = fid; Valid = valid; Mode = mode; Uid = uid; Gid = gid; FileSize = fileSize; AtimeSec = atimeSec; AtimeNsec = atimeNsec; MtimeSec = mtimeSec; MtimeNsec = mtimeNsec }

