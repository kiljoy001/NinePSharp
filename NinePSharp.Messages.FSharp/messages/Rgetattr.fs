namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Rgetattr =
    { Size: uint32
      Tag: uint16
      Valid: uint64
      Qid: Qid
      Mode: uint32
      Uid: uint32
      Gid: uint32
      Nlink: uint64
      Rdev: uint64
      FileSize: uint64
      Blksize: uint64
      Blocks: uint64
      AtimeSec: uint64
      AtimeNsec: uint64
      MtimeSec: uint64
      MtimeNsec: uint64
      CtimeSec: uint64
      CtimeNsec: uint64
      BtimeSec: uint64
      BtimeNsec: uint64
      Gen: uint64
      DataVersion: uint64 }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Rgetattr
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Rgetattr span &offset
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Valid); offset <- offset + 8
            writeQid this.Qid span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Mode); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Uid); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Gid); offset <- offset + 4
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Nlink); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Rdev); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.FileSize); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Blksize); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Blocks); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.AtimeSec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.AtimeNsec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.MtimeSec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.MtimeNsec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.CtimeSec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.CtimeNsec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.BtimeSec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.BtimeNsec); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Gen); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.DataVersion)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let valid = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let qid = readQid data &offset
        let mode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let uid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let gid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let nlink = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let rdev = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let fileSize = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let blksize = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let blocks = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let atimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let atimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let mtimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let mtimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let ctimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let ctimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let btimeSec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let btimeNsec = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let gen = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let dataVersion = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        { Size = size; Tag = tag; Valid = valid; Qid = qid; Mode = mode; Uid = uid; Gid = gid; Nlink = nlink; Rdev = rdev; FileSize = fileSize; Blksize = blksize; Blocks = blocks; AtimeSec = atimeSec; AtimeNsec = atimeNsec; MtimeSec = mtimeSec; MtimeNsec = mtimeNsec; CtimeSec = ctimeSec; CtimeNsec = ctimeNsec; BtimeSec = btimeSec; BtimeNsec = btimeNsec; Gen = gen; DataVersion = dataVersion }

