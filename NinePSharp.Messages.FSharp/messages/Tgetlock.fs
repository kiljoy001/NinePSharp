namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Tgetlock =
    { Size: uint32
      Tag: uint16
      Fid: uint32
      LockType: byte
      Start: uint64
      Length: uint64
      ProcId: uint32
      ClientId: string }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Tgetlock
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Tgetlock span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Fid); offset <- offset + 4
            span.[offset] <- this.LockType
            offset <- offset + 1
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Start); offset <- offset + 8
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), this.Length); offset <- offset + 8
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.ProcId); offset <- offset + 4
            writeString this.ClientId span &offset

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let lockType = data.[offset]
        offset <- offset + 1
        let start = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let length = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8))
        offset <- offset + 8
        let procId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let clientId = readString data &offset
        { Size = size; Tag = tag; Fid = fid; LockType = lockType; Start = start; Length = length; ProcId = procId; ClientId = clientId }

