#r "/home/scott/Repo/NinePSharp/NinePSharp.Tests/bin/Debug/net10.0/FsCheck.dll"
open FsCheck

try
    let g = Gen.constant 1
    printfn "Gen.constant found"
    let g2 = Gen.map (fun x -> x + 1) g
    printfn "Gen.map found"
with
| ex -> printfn "Error: %s" ex.Message

// List members of Gen if possible
let t = typeof<Gen<int>>.DeclaringType
if t <> null then printfn "DeclaringType: %s" t.FullName
else printfn "No DeclaringType"

// Check for Gen module
let asm = typeof<Gen<int>>.Assembly
let genModule = asm.GetTypes() |> Array.tryFind (fun t -> t.Name = "Gen" && t.IsAbstract && t.IsSealed)
match genModule with
| Some m -> 
    printfn "Found Gen Module: %s" m.FullName
    m.GetMethods() |> Array.iter (fun x -> printfn "  Member: %s" x.Name)
| None -> printfn "Gen Module not found"
