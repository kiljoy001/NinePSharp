namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Twstat =
    { Size: uint32
      Tag: uint16
      Fid: uint32
      Stat: Stat }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Twstat
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Twstat span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Fid); offset <- offset + 4
            this.Stat.WriteTo(span, &offset)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let stat = Stat(data, &offset)
        { Size = size; Tag = tag; Fid = fid; Stat = stat }

