namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Tattach =
    { Size: uint32
      Tag: uint16
      Fid: uint32
      Afid: uint32
      Uname: string
      Aname: string }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Tattach
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Tattach span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Fid); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Afid); offset <- offset + 4
            writeString this.Uname span &offset
            writeString this.Aname span &offset

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let fid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let afid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let uname = readString data &offset
        let aname = readString data &offset
        { Size = size; Tag = tag; Fid = fid; Afid = afid; Uname = uname; Aname = aname }

