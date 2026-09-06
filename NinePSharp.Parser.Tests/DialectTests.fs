module NinePSharp.Parser.Tests.DialectTests

open Xunit
open FsCheck.Xunit
open NinePSharp.Constants
open NinePSharp.Parser

[<Fact>]
let ``fromString recognizes 9P2000L`` () =
    let result = Dialect.fromString "9P2000.L"
    Assert.Equal(NinePDialect.NineP2000L, result)

[<Fact>]
let ``fromString recognizes 9P2000L with prefix`` () =
    let result = Dialect.fromString "version:9P2000.L:extra"
    Assert.Equal(NinePDialect.NineP2000L, result)

[<Fact>]
let ``fromString recognizes 9P2000u`` () =
    let result = Dialect.fromString "9P2000.u"
    Assert.Equal(NinePDialect.NineP2000U, result)

[<Fact>]
let ``fromString recognizes 9P2000u with prefix`` () =
    let result = Dialect.fromString "prefix:9P2000.u"
    Assert.Equal(NinePDialect.NineP2000U, result)

[<Fact>]
let ``fromString defaults to 9P2000 for unknown`` () =
    let result = Dialect.fromString "9P2000"
    Assert.Equal(NinePDialect.NineP2000, result)

[<Fact>]
let ``fromString defaults to 9P2000 for empty`` () =
    let result = Dialect.fromString ""
    Assert.Equal(NinePDialect.NineP2000, result)

[<Fact>]
let ``fromString defaults to 9P2000 for garbage`` () =
    let result = Dialect.fromString "completely-wrong-version"
    Assert.Equal(NinePDialect.NineP2000, result)

[<Fact>]
let ``is9u returns true for NineP2000U`` () =
    Assert.True(Dialect.is9u NinePDialect.NineP2000U)

[<Fact>]
let ``is9u returns true for NineP2000L`` () =
    Assert.True(Dialect.is9u NinePDialect.NineP2000L)

[<Fact>]
let ``is9u returns false for NineP2000`` () =
    Assert.False(Dialect.is9u NinePDialect.NineP2000)

[<Property>]
let ``fromString always returns a valid dialect`` (s: string) =
    let s = if isNull s then "" else s
    let result = Dialect.fromString s
    result = NinePDialect.NineP2000 || result = NinePDialect.NineP2000U || result = NinePDialect.NineP2000L

[<Property>]
let ``fromString prioritizes 9P2000L over 9P2000u when both present`` (prefix: string, suffix: string) =
    let prefix = if isNull prefix then "" else prefix
    let suffix = if isNull suffix then "" else suffix
    let combined = prefix + "9P2000.L" + "9P2000.u" + suffix
    let result = Dialect.fromString combined
    result = NinePDialect.NineP2000L
