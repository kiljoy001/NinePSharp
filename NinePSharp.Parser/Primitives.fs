namespace NinePSharp.Parser

open System
open System.Buffers.Binary
open NinePSharp.Protocol
open NinePSharp.Constants

module Primitives =
    
    let readUInt16 (span: ReadOnlySpan<byte>) (offset: byref<int>) : uint16 =
        let v = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2))
        offset <- offset + 2
        v

    let readUInt32 (span: ReadOnlySpan<byte>) (offset: byref<int>) : uint32 =
        let v = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4))
        offset <- offset + 4
        v

    let readUInt64 (span: ReadOnlySpan<byte>) (offset: byref<int>) : uint64 =
        let v = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8))
        offset <- offset + 8
        v

    let readString (span: ReadOnlySpan<byte>) (offset: byref<int>) : string =
        span.ReadString(&offset)

    let readQid (span: ReadOnlySpan<byte>) (offset: byref<int>) : Qid =
        let qType : QidType = LanguagePrimitives.EnumOfValue (span.[offset])
        let version = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 1, 4))
        let path = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset + 5, 8))
        offset <- offset + 13
        Qid(qType, version, path)
