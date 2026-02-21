namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Rwalk =
    { Size: uint32
      Tag: uint16
      Wqid: Qid[] }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Rwalk
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Rwalk span &offset
            let len = match this.Wqid with | null -> 0us | x -> uint16 x.Length
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), len); offset <- offset + 2
            if not (isNull this.Wqid) then
                for qid in this.Wqid do
                    writeQid qid span &offset

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let nwqid = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2))
        offset <- offset + 2
        let wqid = Array.zeroCreate (int nwqid)
        for i = 0 to int nwqid - 1 do
            wqid.[i] <- readQid data &offset
        { Size = size; Tag = tag; Wqid = wqid }

