namespace NinePSharp.Parser

open NinePSharp.Messages
open NinePSharp.Constants
open System

module NinePParser =
    
    // Internal parser returning structured error
    let internal parseInternal (dialect: NinePDialect) (data: ReadOnlyMemory<byte>) : Result<NinePMessage, ParseError> =
        if data.Length < int NinePConstants.HeaderSize then
            Error TooShort
        else
            let span = data.Span
            try
                let size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4))
                
                if size > uint32 data.Length then
                    Error (TruncatedMessage(size, data.Length))
                else
                    let msgType = span.[4]
                    let msgData = data.Slice(0, int size)
                    
                    let result = 
                        match dialect with
                        | NinePDialect.NineP2000L -> Linux.parse msgType msgData
                        | _ -> Classic.parse msgType msgData dialect
                    
                    match result with
                    | Ok msg -> 
                        match Validation.validate msg with
                        | Ok () -> Ok msg
                        | Error err -> Error (ValidationFailed err)
                    | Error err -> Error err
            with
            | :? System.IndexOutOfRangeException -> Error MalformedBounds
            | ex -> Error (InternalException ex.Message)

    // Public API now uses the enum for clarity
    let parse (dialect: NinePDialect) (data: ReadOnlyMemory<byte>) : Result<NinePMessage, string> =
        match parseInternal dialect data with
        | Ok msg -> Ok msg
        | Error err -> Error err.Message
