namespace NinePSharp.Parser

open NinePSharp.Constants

[<RequireQualifiedAccess>]
module Dialect =
    let fromString (s: string) =
        match s with
        | s when s.Contains("9P2000.L") -> NinePDialect.NineP2000L
        | s when s.Contains("9P2000.u") -> NinePDialect.NineP2000U
        | _ -> NinePDialect.NineP2000

    let is9u (d: NinePDialect) = 
        d = NinePDialect.NineP2000U || d = NinePDialect.NineP2000L
