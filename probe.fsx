#r "/home/scott/Repo/NinePSharp/NinePSharp.Tests/bin/Debug/net10.0/FsCheck.dll"
open FsCheck
printfn "Assembly: %s" (typeof<Gen<int>>.Assembly.FullName)
typeof<Gen<int>>.Assembly.GetTypes()
|> Array.filter (fun t -> t.IsPublic && (t.Name.Contains("Gen") || t.Name.Contains("Arb")))
|> Array.iter (fun t -> printfn "Type: %s" t.FullName)
