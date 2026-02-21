namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Tfsync =
    { Size: uint32
      Tag: uint16
      Fid: uint32
      Datasync: uint32 }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Tfsync
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Tfsync span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Fid); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Datasync)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let datasync = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        { Size = size; Tag = tag; Fid = fid; Datasync = datasync }

