namespace NinePSharp.Parser

type NinePDialect = 
    | NineP2000      = 0
    | NineP2000U     = 1
    | NineP2000L     = 2

[<RequireQualifiedAccess>]
module Dialect =
    let fromString (s: string) =
        match s with
        | s when s.Contains("9P2000.L") -> NinePDialect.NineP2000L
        | s when s.Contains("9P2000.u") -> NinePDialect.NineP2000U
        | _ -> NinePDialect.NineP2000

    let is9u (d: NinePDialect) = 
        d = NinePDialect.NineP2000U || d = NinePDialect.NineP2000L
