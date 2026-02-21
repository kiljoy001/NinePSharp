namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Rlcreate =
    { Size: uint32
      Tag: uint16
      Qid: Qid
      Iounit: uint32 }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Rlcreate
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Rlcreate span &offset
            writeQid this.Qid span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Iounit)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let qid = readQid data &offset
        let iounit = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        { Size = size; Tag = tag; Qid = qid; Iounit = iounit }

