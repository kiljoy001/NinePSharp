namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Tversion =
    { Size: uint32
      Tag: uint16
      MSize: uint32
      Version: string }
    
    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Tversion
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Tversion span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.MSize); offset <- offset + 4
            writeString this.Version span &offset

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let msize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let version = readString data &offset
        { Size = size; Tag = tag; MSize = msize; Version = version }

