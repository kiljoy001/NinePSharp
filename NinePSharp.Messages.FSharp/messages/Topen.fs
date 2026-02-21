namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Topen =
    { Size: uint32
      Tag: uint16
      Fid: uint32
      Mode: byte }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Topen
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Topen span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Fid); offset <- offset + 4
            span.[offset] <- this.Mode; offset <- offset + 1

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let mode = data.[offset]
        offset <- offset + 1
        { Size = size; Tag = tag; Fid = fid; Mode = mode }

