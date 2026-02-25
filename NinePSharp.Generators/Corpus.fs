namespace NinePSharp.Generators

open System
open System.IO
open FsCheck
open NinePSharp.Messages
open NinePSharp.Parser
open System.Linq

module Corpus =
    
    let generateMessage (gen: Gen<NinePMessage>) =
        Gen.sample 10 1 gen |> Seq.head

    let generateCorpus (outputDir: string) =
        if not (Directory.Exists(outputDir)) then Directory.CreateDirectory(outputDir) |> ignore

        let messageTypes = [
            // Classic 9P2000 T-messages
            ("tversion", Generators.genTversion)
            ("tauth", Generators.genTauth)
            ("tflush", Generators.genTflush)
            ("tattach", Generators.genTattach)
            ("twalk", Generators.genTwalk)
            ("topen", Generators.genTopen)
            ("tcreate", Generators.genTcreate)
            ("tread", Generators.genTread)
            ("twrite", Generators.genTwrite)
            ("tclunk", Generators.genTclunk)
            ("tremove", Generators.genTremove)
            ("tstat", Generators.genTstat)
            ("twstat", Generators.genTwstat)
            // Classic 9P2000 R-messages
            ("rversion", Generators.genRversion)
            ("rauth", Generators.genRauth)
            ("rattach", Generators.genRattach)
            ("rerror", Generators.genRerror)
            ("ropen", Generators.genRopen)
            ("rcreate", Generators.genRcreate)
            ("rread", Generators.genRread)
            ("rwrite", Generators.genRwrite)
            ("rclunk", Generators.genRclunk)
            ("rflush", Generators.genRflush)
            ("rremove", Generators.genRremove)
            ("rwstat", Generators.genRwstat)
            ("rwalk", Generators.genRwalk)
            ("rstat", Generators.genRstat)
            // 9P2000.L T-messages
            ("tstatfs", Generators.genTstatfs)
            ("tlopen", Generators.genTlopen)
            ("tlcreate", Generators.genTlcreate)
            ("tsymlink", Generators.genTsymlink)
            ("tmknod", Generators.genTmknod)
            ("trename", Generators.genTrename)
            ("treadlink", Generators.genTreadlink)
            ("tgetattr", Generators.genTgetattr)
            ("tsetattr", Generators.genTsetattr)
            ("txattrwalk", Generators.genTxattrwalk)
            ("txattrcreate", Generators.genTxattrcreate)
            ("treaddir", Generators.genTreaddir)
            ("tfsync", Generators.genTfsync)
            ("tlock", Generators.genTlock)
            ("tgetlock", Generators.genTgetlock)
            ("tlink", Generators.genTlink)
            ("tmkdir", Generators.genTmkdir)
            ("trenameat", Generators.genTrenameat)
            ("tunlinkat", Generators.genTunlinkat)
            // 9P2000.L R-messages
            ("rlerror", Generators.genRlerror)
            ("rstatfs", Generators.genRstatfs)
            ("rlopen", Generators.genRlopen)
            ("rlcreate", Generators.genRlcreate)
            ("rsymlink", Generators.genRsymlink)
            ("rmknod", Generators.genRmknod)
            ("rrename", Generators.genRrename)
            ("rreadlink", Generators.genRreadlink)
            ("rgetattr", Generators.genRgetattr)
            ("rsetattr", Generators.genRsetattr)
            ("rxattrwalk", Generators.genRxattrwalk)
            ("rxattrcreate", Generators.genRxattrcreate)
            ("rreaddir", Generators.genRreaddir)
            ("rfsync", Generators.genRfsync)
            ("rlock", Generators.genRlock)
            ("rgetlock", Generators.genRgetlock)
            ("rlink", Generators.genRlink)
            ("rmkdir", Generators.genRmkdir)
            ("rrenameat", Generators.genRrenameat)
            ("runlinkat", Generators.genRunlinkat)
        ]

        // Generate multiple valid samples per message type
        for (name, gen) in messageTypes do
            for i in 1..5 do
                let msg = generateMessage gen
                let bytes = Serializer.serialize msg
                File.WriteAllBytes(Path.Combine(outputDir, sprintf "%s_valid_%03d.bin" name i), bytes)

    let generateEdgeCases (outputDir: string) =
        if not (Directory.Exists(outputDir)) then Directory.CreateDirectory(outputDir) |> ignore

        // Comprehensive edge cases
        let edgeCases = [
            // Size boundary values
            ("tversion_size_zero", MsgTversion(Tversion(0us, 0u, "9P2000.L")))
            ("tversion_size_max", MsgTversion(Tversion(0us, UInt32.MaxValue, "9P2000.L")))
            ("tversion_msize_small", MsgTversion(Tversion(0us, 1u, "9P2000.L")))
            ("tversion_msize_7", MsgTversion(Tversion(0us, 7u, "9P2000.L")))  // Header size

            // String boundary values
            ("tauth_null_uname", MsgTauth(Tauth(0us, 0u, null, "")))
            ("tauth_empty_uname", MsgTauth(Tauth(0us, 0u, "", "")))
            ("tauth_long_uname", MsgTauth(Tauth(0us, 0u, String('A', 10000), "aname")))

            // Path traversal
            ("twalk_parent_traversal", MsgTwalk(Twalk(0us, 1u, 2u, [| ".."; ".."; ".."; "etc"; "passwd" |])))
            ("twalk_absolute_path", MsgTwalk(Twalk(0us, 1u, 2u, [| "/"; "etc"; "shadow" |])))
            ("twalk_empty_components", MsgTwalk(Twalk(0us, 1u, 2u, [| ""; ""; "" |])))
            ("twalk_oversized", MsgTwalk(Twalk(0us, 1u, 2u, Array.init 100 (fun i -> sprintf "dir%d" i))))

            // FID boundary values
            ("tread_fid_max", MsgTread(Tread(0us, UInt32.MaxValue, 0uL, 8192u)))
            ("twrite_fid_zero", MsgTwrite(Twrite(0us, 0u, 0uL, [| 0x41uy; 0x42uy |])))

            // Offset boundary values
            ("tread_offset_max", MsgTread(Tread(0us, 1u, UInt64.MaxValue, 8192u)))
            ("twrite_offset_max", MsgTwrite(Twrite(0us, 1u, UInt64.MaxValue, ReadOnlyMemory<byte>([| 0x41uy |]))))

            // Count boundary values
            ("tread_count_zero", MsgTread(Tread(0us, 1u, 0uL, 0u)))
            ("tread_count_max", MsgTread(Tread(0us, 1u, 0uL, UInt32.MaxValue)))

            // Data size variations
            ("twrite_empty_data", MsgTwrite(Twrite(0us, 1u, 0uL, ReadOnlyMemory<byte>([||]))))
            ("twrite_large_data", MsgTwrite(Twrite(0us, 1u, 0uL, ReadOnlyMemory<byte>(Array.zeroCreate 65536))))

            // 9P2000.L specific (calculate proper sizes)
            ("tlcreate_invalid_name",
                let name = "/etc/passwd"
                let size = 7u + 4u + 2u + uint32 (Text.Encoding.UTF8.GetByteCount(name)) + 4u + 4u + 4u
                MsgTlcreate(Tlcreate(size, 0us, 1u, name, 0u, 0644u, 0u)))
            ("tlcreate_empty_name",
                let size = 7u + 4u + 2u + 4u + 4u + 4u
                MsgTlcreate(Tlcreate(size, 0us, 1u, "", 0u, 0644u, 0u)))
            ("tlcreate_long_name",
                let name = String('X', 1000)
                let size = 7u + 4u + 2u + uint32 (Text.Encoding.UTF8.GetByteCount(name)) + 4u + 4u + 4u
                MsgTlcreate(Tlcreate(size, 0us, 1u, name, 0u, 0644u, 0u)))

            ("tsetattr_all_max",
                let size = 7u + 4u + 4u + 4u + 4u + 4u + 8u + 8u + 8u + 8u + 8u
                MsgTsetattr(Tsetattr(size, 0us, 1u, UInt32.MaxValue, UInt32.MaxValue, UInt32.MaxValue, UInt32.MaxValue, UInt64.MaxValue, UInt64.MaxValue, UInt64.MaxValue, UInt64.MaxValue, UInt64.MaxValue)))

            ("trenameat_traversal",
                let oldName = "../../../etc/passwd"
                let newName = "shadow"
                let size = 7u + 4u + 2u + uint32 (Text.Encoding.UTF8.GetByteCount(oldName)) + 4u + 2u + uint32 (Text.Encoding.UTF8.GetByteCount(newName))
                MsgTrenameat(Trenameat(size, 0us, 1u, oldName, 1u, newName)))
        ]

        for (name, msg) in edgeCases do
            let bytes = Serializer.serialize msg
            File.WriteAllBytes(Path.Combine(outputDir, sprintf "%s.bin" name), bytes)

    let generateBackendCorpus (outputDir: string) =
        if not (Directory.Exists(outputDir)) then Directory.CreateDirectory(outputDir) |> ignore

        // Backend-specific test sequences
        let backendSequences = [
            // MockFS sequence
            ("mockfs_create_write_read",
                let name = "testfile.txt"
                let size = 7u + 4u + 2u + uint32 (Text.Encoding.UTF8.GetByteCount(name)) + 4u + 4u + 4u
                [
                    MsgTlcreate(Tlcreate(size, 100us, 1u, name, 0u, 0644u, 0u))
                    MsgTwrite(Twrite(101us, 1u, 0uL, ReadOnlyMemory<byte>(Text.Encoding.UTF8.GetBytes("Hello World"))))
                    MsgTread(Tread(102us, 1u, 0uL, 8192u))
                ])

            // SecretFS sequence
            ("secretfs_provision_unlock", [
                MsgTwalk(Twalk(100us, 1u, 2u, [| "provision" |]))
                MsgTwrite(Twrite(101us, 2u, 0uL, ReadOnlyMemory<byte>(Text.Encoding.UTF8.GetBytes("test_secret"))))
                MsgTwalk(Twalk(102us, 1u, 3u, [| "unlock" |]))
                MsgTwrite(Twrite(103us, 3u, 0uL, ReadOnlyMemory<byte>(Text.Encoding.UTF8.GetBytes("password123"))))
            ])

            // Traversal attack
            ("backend_path_traversal", [
                MsgTwalk(Twalk(100us, 1u, 2u, [| ".."; ".."; ".."; "etc" |]))
                MsgTwalk(Twalk(101us, 2u, 3u, [| "passwd" |]))
                MsgTread(Tread(102us, 3u, 0uL, 8192u))
            ])
        ]

        for (name, msgs) in backendSequences do
            let allBytes = msgs |> List.map Serializer.serialize |> Array.concat
            File.WriteAllBytes(Path.Combine(outputDir, sprintf "%s.bin" name), allBytes)

module Program =
    [<EntryPoint>]
    let main args =
        if args.Length > 0 && args.[0] = "generate-corpus" then
            let outputDir = if args.Length > 1 then args.[1] else "corpus"
            printfn "Generating corpus into %s..." outputDir

            let validDir = Path.Combine(outputDir, "parser", "valid")
            let edgeDir = Path.Combine(outputDir, "parser", "edge_cases")
            let backendDir = Path.Combine(outputDir, "backend")

            printfn "Generating valid message corpus..."
            Corpus.generateCorpus validDir

            printfn "Generating edge case corpus..."
            Corpus.generateEdgeCases edgeDir

            printfn "Generating backend-specific corpus..."
            Corpus.generateBackendCorpus backendDir

            printfn "Corpus generation complete!"
            printfn "  - Valid messages: %s" validDir
            printfn "  - Edge cases: %s" edgeDir
            printfn "  - Backend tests: %s" backendDir
            0
        else
            printfn "Usage: dotnet run --project NinePSharp.Generators -- generate-corpus [outputDir]"
            1
