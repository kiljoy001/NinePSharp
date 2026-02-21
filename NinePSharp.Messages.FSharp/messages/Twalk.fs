namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Twalk =
    { Size: uint32
      Tag: uint16
      Fid: uint32
      NewFid: uint32
      Wname: string[] }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Twalk
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Twalk span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Fid); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.NewFid); offset <- offset + 4
            let len = match this.Wname with | null -> 0us | x -> uint16 x.Length
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), len); offset <- offset + 2
            if not (isNull this.Wname) then
                for name in this.Wname do
                    writeString name span &offset

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let newfid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let nwname = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2))
        offset <- offset + 2
        let wname = Array.zeroCreate (int nwname)
        for i = 0 to int nwname - 1 do
            wname.[i] <- readString data &offset
        { Size = size; Tag = tag; Fid = fid; NewFid = newfid; Wname = wname }

