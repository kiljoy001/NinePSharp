namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Trenameat =
    { Size: uint32
      Tag: uint16
      OldDirFid: uint32
      OldName: string
      NewDirFid: uint32
      NewName: string }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Trenameat
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Trenameat span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.OldDirFid); offset <- offset + 4
            writeString this.OldName span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.NewDirFid); offset <- offset + 4
            writeString this.NewName span &offset

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let oldDirFid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let oldName = readString data &offset
        let newDirFid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let newName = readString data &offset
        { Size = size; Tag = tag; OldDirFid = oldDirFid; OldName = oldName; NewDirFid = newDirFid; NewName = newName }

