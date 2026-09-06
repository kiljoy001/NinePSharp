module NinePSharp.Parser.Tests.ValidationTests

open Xunit
open FsCheck.Xunit
open System
open NinePSharp.Messages
open NinePSharp.Constants
open NinePSharp.Parser

[<Fact>]
let ``validateMSize rejects size below HeaderSize`` () =
    let msg = MsgTversion(Tversion(1us, uint32 (int NinePConstants.HeaderSize - 1), "9P2000"))
    match Validation.validate msg with
    | Error err -> Assert.Contains("MSize too small", err)
    | Ok () -> Assert.Fail("Expected validation failure for undersized msize")

[<Fact>]
let ``validateMSize accepts HeaderSize exactly`` () =
    let msg = MsgTversion(Tversion(1us, uint32 NinePConstants.HeaderSize, "9P2000"))
    match Validation.validate msg with
    | Ok () -> ()
    | Error err -> Assert.Fail($"Expected validation success, got: {err}")

[<Fact>]
let ``validateMSize accepts size above HeaderSize`` () =
    let msg = MsgTversion(Tversion(1us, 8192u, "9P2000"))
    match Validation.validate msg with
    | Ok () -> ()
    | Error err -> Assert.Fail($"Expected validation success, got: {err}")

[<Fact>]
let ``validateString rejects oversized version string`` () =
    let oversized = String.replicate (Validation.MaxStringLength + 1) "a"
    let msg = MsgTversion(Tversion(1us, 8192u, oversized))
    match Validation.validate msg with
    | Error err -> Assert.Contains("Invalid", err)
    | Ok () -> Assert.Fail("Expected validation failure for oversized version")

[<Fact>]
let ``validateString accepts MaxStringLength exactly`` () =
    let exact = String.replicate Validation.MaxStringLength "a"
    let msg = MsgTversion(Tversion(1us, 8192u, exact))
    match Validation.validate msg with
    | Ok () -> ()
    | Error err -> Assert.Fail($"Expected validation success for max-length string, got: {err}")

[<Fact>]
let ``validateString accepts empty string`` () =
    let msg = MsgTversion(Tversion(1us, 8192u, ""))
    match Validation.validate msg with
    | Ok () -> ()
    | Error err -> Assert.Fail($"Expected validation success for empty string, got: {err}")

[<Fact>]
let ``validateTauth rejects oversized uname`` () =
    let oversized = String.replicate (Validation.MaxStringLength + 1) "a"
    let msg = MsgTauth(Tauth(1us, 10u, oversized, "", Nullable()))
    match Validation.validate msg with
    | Error err -> Assert.Contains("Invalid uname", err)
    | Ok () -> Assert.Fail("Expected validation failure for oversized uname")

[<Fact>]
let ``validateTauth rejects oversized aname`` () =
    let oversized = String.replicate (Validation.MaxStringLength + 1) "a"
    let msg = MsgTauth(Tauth(1us, 10u, "user", oversized, Nullable()))
    match Validation.validate msg with
    | Error err -> Assert.Contains("Invalid aname", err)
    | Ok () -> Assert.Fail("Expected validation failure for oversized aname")

[<Fact>]
let ``validateTauth accepts valid uname and aname`` () =
    let msg = MsgTauth(Tauth(1us, 10u, "root", "", Nullable()))
    match Validation.validate msg with
    | Ok () -> ()
    | Error err -> Assert.Fail($"Expected validation success, got: {err}")

[<Fact>]
let ``validateTattach rejects oversized uname`` () =
    let oversized = String.replicate (Validation.MaxStringLength + 1) "a"
    let msg = MsgTattach(Tattach(1us, 1u, 0u, oversized, ""))
    match Validation.validate msg with
    | Error err -> Assert.Contains("Invalid uname", err)
    | Ok () -> Assert.Fail("Expected validation failure for oversized uname")

[<Fact>]
let ``validateTattach rejects oversized aname`` () =
    let oversized = String.replicate (Validation.MaxStringLength + 1) "a"
    let msg = MsgTattach(Tattach(1us, 1u, 0u, "user", oversized))
    match Validation.validate msg with
    | Error err -> Assert.Contains("Invalid aname", err)
    | Ok () -> Assert.Fail("Expected validation failure for oversized aname")

[<Fact>]
let ``validateTattach accepts valid uname and aname`` () =
    let msg = MsgTattach(Tattach(1us, 1u, 0u, "root", "/"))
    match Validation.validate msg with
    | Ok () -> ()
    | Error err -> Assert.Fail($"Expected validation success, got: {err}")

[<Fact>]
let ``validateWalkComponents rejects too many components`` () =
    let walk = Twalk(1us, 1u, 2u, Array.init 17 (fun i -> $"p{i}"))
    match Validation.validate (MsgTwalk walk) with
    | Error err -> Assert.Contains("Too many walk components", err)
    | Ok () -> Assert.Fail("Expected validation failure for oversized walk")

[<Fact>]
let ``validateWalkComponents accepts MaxWalkComponents exactly`` () =
    let walk = Twalk(1us, 1u, 2u, Array.init 16 (fun i -> $"p{i}"))
    match Validation.validate (MsgTwalk walk) with
    | Ok () -> ()
    | Error err -> Assert.Fail($"Expected validation success for 16 components, got: {err}")

[<Fact>]
let ``validateWalkComponents rejects oversized component`` () =
    let oversized = String.replicate (Validation.MaxStringLength + 1) "a"
    let walk = Twalk(1us, 1u, 2u, [| "a"; oversized; "c" |])
    match Validation.validate (MsgTwalk walk) with
    | Error err -> Assert.Contains("Invalid walk component string", err)
    | Ok () -> Assert.Fail("Expected validation failure for oversized component")

[<Fact>]
let ``validateWalkComponents accepts empty array`` () =
    let walk = Twalk(1us, 1u, 2u, [||])
    match Validation.validate (MsgTwalk walk) with
    | Ok () -> ()
    | Error err -> Assert.Fail($"Expected validation success for empty walk, got: {err}")

[<Fact>]
let ``validateWalkComponents accepts single component`` () =
    let walk = Twalk(1us, 1u, 2u, [| "file.txt" |])
    match Validation.validate (MsgTwalk walk) with
    | Ok () -> ()
    | Error err -> Assert.Fail($"Expected validation success for single component, got: {err}")

[<Fact>]
let ``validateWalkComponents rejects null arrays`` () =
    let walk = Twalk(1us, 1u, 2u, Unchecked.defaultof<string[]>)
    match Validation.validate (MsgTwalk walk) with
    | Error err -> Assert.Contains("Invalid walk components", err)
    | Ok () -> Assert.Fail("Expected validation failure for null walk array")

[<Fact>]
let ``validateString uses UTF8 byte length instead of character length`` () =
    let oversizedByBytes = String.replicate ((Validation.MaxStringLength / 2) + 1) "\u00e9"
    Assert.True(oversizedByBytes.Length <= Validation.MaxStringLength)

    let msg = MsgTversion(Tversion(1us, 8192u, oversizedByBytes))
    match Validation.validate msg with
    | Error err -> Assert.Contains("Invalid version string", err)
    | Ok () -> Assert.Fail("Expected validation failure for string over the UTF8 byte limit")

[<Fact>]
let ``validate rejects oversized strings in extended requests`` () =
    let oversized = String.replicate (Validation.MaxStringLength + 1) "a"
    let msg = MsgTsymlink(Tsymlink(0u, 1us, 1u, oversized, "target", 0u))
    match Validation.validate msg with
    | Error err -> Assert.Contains("Invalid name", err)
    | Ok () -> Assert.Fail("Expected validation failure for oversized symlink name")

[<Fact>]
let ``validate rejects oversized stat strings`` () =
    let oversized = String.replicate (Validation.MaxStringLength + 1) "a"
    let stat = Stat(0us, 0us, 0u, Qid(QidType.QTFILE, 0u, 1UL), 0u, 0u, 0u, 0UL, oversized, "u", "g", "m", NinePDialect.NineP2000)
    let msg = MsgTwstat(Twstat(1us, 1u, stat))
    match Validation.validate msg with
    | Error err -> Assert.Contains("Invalid stat.name", err)
    | Ok () -> Assert.Fail("Expected validation failure for oversized stat name")

[<Fact>]
let ``validate rejects too many Rwalk qids`` () =
    let qids = Array.init (Validation.MaxWalkComponents + 1) (fun i -> Qid(QidType.QTFILE, 0u, uint64 i))
    let msg = MsgRwalk(Rwalk(1us, qids))
    match Validation.validate msg with
    | Error err -> Assert.Contains("Too many walk qids", err)
    | Ok () -> Assert.Fail("Expected validation failure for oversized Rwalk qid array")

[<Fact>]
let ``validate passes through unvalidated message types`` () =
    let msg = MsgTflush(Tflush(1us, 2us))
    match Validation.validate msg with
    | Ok () -> ()
    | Error err -> Assert.Fail($"Expected validation success for Tflush, got: {err}")

[<Property>]
let ``validateString always returns Ok for valid non-null strings under MaxStringLength`` (s: string) =
    if not (isNull s) && s.Length <= Validation.MaxStringLength then
        let msg = MsgTversion(Tversion(1us, 8192u, s))
        match Validation.validate msg with
        | Ok () -> true
        | Error _ -> false
    else
        true // Skip invalid inputs for this property

[<Property>]
let ``validateWalkComponents always returns Ok for valid arrays`` (names: string[]) =
    if not (isNull names)
       && names.Length <= 16
       && names |> Array.forall (fun n -> not (isNull n) && n.Length <= Validation.MaxStringLength) then
        let walk = Twalk(1us, 1u, 2u, names)
        match Validation.validate (MsgTwalk walk) with
        | Ok () -> true
        | Error _ -> false
    else
        true // Skip invalid inputs for this property
