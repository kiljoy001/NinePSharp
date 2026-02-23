namespace NinePSharp.Parser

type ParseError = 
    | TruncatedMessage of reportedSize:uint32 * actualSize:int
    | InvalidSize of uint32
    | UnknownMessageType of byte
    | InvalidString of string
    | MalformedBounds
    | ValidationFailed of string
    | InternalException of string
    | TooShort
    
    member this.Message =
        match this with
        | TruncatedMessage (r, a) -> sprintf "Message truncated: reported %u, buffer %d" r a
        | InvalidSize s -> sprintf "Invalid message size: %u" s
        | UnknownMessageType t -> sprintf "Unknown message type: %d" t
        | InvalidString s -> sprintf "Invalid 9P string: %s" s
        | MalformedBounds -> "Malformed message bounds"
        | ValidationFailed err -> sprintf "Validation error: %s" err
        | InternalException msg -> sprintf "Parser exception: %s" msg
        | TooShort -> "Message too short"
