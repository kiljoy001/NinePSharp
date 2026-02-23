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
                        match Classic.parse msgType msgData dialect with
                        | Ok msg -> Ok msg
                        | Error _ -> 
                            match Linux.parse msgType msgData with
                            | Ok msg -> Ok msg
                            | Error _ -> Error (UnknownMessageType msgType)
                    
                    match result with
                    | Ok msg -> 
                        match Validation.validate msg with
                        | Ok () -> Ok msg
                        | Error err -> Error (ValidationFailed err)
                    | Error err -> Error err
            with
            | :? System.IndexOutOfRangeException -> Error MalformedBounds
            | ex -> Error (InternalException ex.Message)

    // Original signature restored for compatibility
    let parse (is9u: bool) (data: ReadOnlyMemory<byte>) : Result<NinePMessage, string> =
        let dialect = if is9u then NinePDialect.NineP2000U else NinePDialect.NineP2000
        match parseInternal dialect data with
        | Ok msg -> Ok msg
        | Error err -> Error err.Message
