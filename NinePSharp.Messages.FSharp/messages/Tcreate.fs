namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Tcreate =
    { Size: uint32
      Tag: uint16
      Fid: uint32
      Name: string
      Perm: uint32
      Mode: byte }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Tcreate
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Tcreate span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Fid); offset <- offset + 4
            writeString this.Name span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Perm); offset <- offset + 4
            span.[offset] <- this.Mode; offset <- offset + 1

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let name = readString data &offset
        let perm = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let mode = data.[offset]
        offset <- offset + 1
        { Size = size; Tag = tag; Fid = fid; Name = name; Perm = perm; Mode = mode }

