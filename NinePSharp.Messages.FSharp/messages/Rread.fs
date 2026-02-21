namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Rread =
    { Size: uint32
      Tag: uint16
      Count: uint32
      Data: byte[] }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Rread
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Rread span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Count); offset <- offset + 4
            if not (isNull this.Data) then
                this.Data.CopyTo(span.Slice(offset))

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let dataArr = data.Slice(offset, int count).ToArray()
        { Size = size; Tag = tag; Count = count; Data = dataArr }

