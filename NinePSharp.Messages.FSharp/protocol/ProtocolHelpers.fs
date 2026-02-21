namespace NinePSharp.Messages.FSharp.Protocol

open System
open System.Buffers.Binary
open NinePSharp.Constants
open NinePSharp.Messages

open System.Text

[<AutoOpen>]
module ProtocolHelpers =
    let inline writeHeaders (size: uint32) (tag: uint16) (msgType: MessageTypes) (span: Span<byte>) (offset: byref<int>) =
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), size)
        span.[4] <- byte msgType
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(5, 2), tag)
        offset <- NinePConstants.HeaderSize

    let inline readHeaders (data: ReadOnlySpan<byte>) (offset: byref<int>) =
        let mutable offset = 0
        let size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4))
        let tag = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2))
        offset <- NinePConstants.HeaderSize
        (size, tag)

    let inline writeString (value: string) (span: Span<byte>) (offset: byref<int>) =
        if isNull value then
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), 0us)
            offset <- offset + 2
        else
            let count = uint16 (Encoding.UTF8.GetByteCount(value))
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), count)
            offset <- offset + 2
            ignore (Encoding.UTF8.GetBytes(value, span.Slice(offset)))
            offset <- offset + int count

    let inline readString (data: ReadOnlySpan<byte>) (offset: byref<int>) =
        let len = int (BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)))
        offset <- offset + 2
        let value = Encoding.UTF8.GetString(data.Slice(offset, len))
        offset <- offset + len
        value

    let inline writeQid (qid: Qid) (span: Span<byte>) (offset: byref<int>) =
        span.[offset] <- byte qid.Type
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset + 1, 4), qid.Version)
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset + 5, 8), qid.Path)
        offset <- offset + 13

    let inline readQid (data: ReadOnlySpan<byte>) (offset: byref<int>) =
        let qType = LanguagePrimitives.EnumOfValue<byte, QidType>(data.[offset])
        let qVer = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 1, 4))
        let qPath = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 5, 8))
        offset <- offset + 13
        Qid(qType, qVer, qPath)