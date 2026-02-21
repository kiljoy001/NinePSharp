namespace NinePSharp.Messages.FSharp

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Interfaces
open NinePSharp.Messages
open NinePSharp.Messages.FSharp.Protocol

type Tmknod =
    { Size: uint32
      Tag: uint16
      Dfid: uint32
      Name: string
      Mode: uint32
      Major: uint32
      Minor: uint32
      Gid: uint32 }

    interface ISerializable with
        member this.Size = this.Size
        member this.Type = MessageTypes.Tmknod
        member this.Tag = this.Tag
        member this.WriteTo(span: Span<byte>) =
            let mutable offset = 0
            writeHeaders this.Size this.Tag MessageTypes.Tmknod span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Dfid); offset <- offset + 4
            writeString this.Name span &offset
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Mode); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Major); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Minor); offset <- offset + 4
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), this.Gid)

    static member Parse (data: ReadOnlySpan<byte>) =
        let mutable offset = 0
        let size, tag = readHeaders data &offset
        let dfid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let name = readString data &offset
        let mode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let major = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let minor = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        offset <- offset + 4
        let gid = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
        { Size = size; Tag = tag; Dfid = dfid; Name = name; Mode = mode; Major = major; Minor = minor; Gid = gid }

