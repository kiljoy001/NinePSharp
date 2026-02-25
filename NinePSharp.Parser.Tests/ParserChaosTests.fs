namespace NinePSharp.Parser.Tests

open Xunit
open FsCheck
open FsCheck.Xunit
open NinePSharp.Parser
open NinePSharp.Constants
open System

module ParserChaosTests =
    
    // Property: The parser must NEVER throw an unhandled exception regardless of the input bytes.
    // It should either return Ok (if the random bytes happen to be a valid message)
    // or Error (for any malformed input).
    [<Property(MaxTest = 10000)>]
    let ``Parser handles arbitrary bytes without crashing`` (data: byte[]) =
        if data = null then true
        else
            try
                let result = NinePParser.parse true (ReadOnlyMemory<byte>(data))
                true
            with
            | ex -> 
                // If we hit here, it's a bug in the parser (likely an IndexOutOfRangeException or similar)
                false

    // Property: Truncated headers must always be rejected gracefully
    [<Property>]
    let ``Parser rejects truncated headers`` (len: int) =
        // len is between 0 and 6 (HeaderSize is 7)
        let actualLen = Math.Abs(len) % 7
        let data = Array.zeroCreate<byte> actualLen
        let result = NinePParser.parse true (ReadOnlyMemory<byte>(data))
        match result with
        | Error err -> err.Contains("short")
        | Ok _ -> false

    // Property: Large reported sizes that exceed buffer must be rejected
    [<Fact>]
    let ``Parser rejects size mismatch`` () =
        let data = [| 0xFFuy; 0xFFuy; 0xFFuy; 0x00uy; byte MessageTypes.Tversion; 0x01uy; 0x00uy |]
        let result = NinePParser.parse true (ReadOnlyMemory<byte>(data))
        match result with
        | Error err -> err.Contains("truncated") || err.Contains("size")
        | Ok _ -> false
