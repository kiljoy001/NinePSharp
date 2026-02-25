open NinePSharp.Generators.Generators
open FsCheck

try
    let arb = NinePArb.NinePMessage()
    printfn "Successfully registered"
    let sample = Gen.sample 1 1 arb.Generator
    printfn "Successfully generated"
with e ->
    printfn "CRASH: %A" e
