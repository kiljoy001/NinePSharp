namespace NinePSharp.Generators

open FsCheck
open NinePSharp.Messages
open NinePSharp.Constants
open NinePSharp.Parser
open System
open System.Linq

module Mutation =
    
    let gen = GenBuilder()

    // ─── Field Mutations ───────────────────────────────────────────────

    let mutateUInt16 (v: uint16) =
        Gen.oneof [
            Gen.constant 0us
            Gen.constant 0xFFFFus
            Gen.choose(0, 65535) |> Gen.map uint16
            Gen.constant (v + 1us)
            Gen.constant (v - 1us)
        ]

    let mutateUInt32 (v: uint32) =
        Gen.oneof [
            Gen.constant 0u
            Gen.constant UInt32.MaxValue
            Gen.choose(0, Int32.MaxValue) |> Gen.map uint32
            Gen.constant (v + 1u)
            Gen.constant (v - 1u)
        ]

    let mutateUInt64 (v: uint64) =
        Gen.oneof [
            Gen.constant 0uL
            Gen.constant UInt64.MaxValue
            Gen.choose(0, Int32.MaxValue) |> Gen.map uint64
            Gen.constant (v + 1uL)
            Gen.constant (v - 1uL)
        ]

    let mutateString (s: string) =
        Gen.oneof [
            Gen.constant ""
            Gen.constant (null : string)
            Gen.arrayOfLength 1000 (Gen.elements ['a'..'z']) |> Gen.map (fun cs -> new string(cs))
            Gen.constant (s + "suffix")
            Gen.constant (if not (isNull s) && s.Length > 0 then s.Substring(0, s.Length - 1) else "")
        ]

    let mutateQid (q: Qid) =
        gen {
            let invalidQidType : QidType = LanguagePrimitives.EnumOfValue 0xFFuy
            let! qType = Gen.elements [QidType.QTDIR; QidType.QTFILE; QidType.QTAPPEND; QidType.QTEXCL; invalidQidType]
            let! version = mutateUInt32 q.Version
            let! path = mutateUInt64 q.Path
            return Qid(qType, version, path)
        }

    // ─── Message Mutations ──────────────────────────────────────────────

    let mutateTversion (t: Tversion) =
        gen {
            let! tag = mutateUInt16 t.Tag
            let! msize = mutateUInt32 t.MSize
            let! version = mutateString t.Version
            return MsgTversion(Tversion(tag, msize, version))
        }

    let mutateTauth (t: Tauth) =
        gen {
            let! tag = mutateUInt16 t.Tag
            let! afid = mutateUInt32 t.Afid
            let! uname = mutateString t.Uname
            let! aname = mutateString t.Aname
            return MsgTauth(Tauth(tag, afid, uname, aname))
        }

    let mutateTwalk (t: Twalk) =
        gen {
            let! tag = mutateUInt16 t.Tag
            let! fid = mutateUInt32 t.Fid
            let! newFid = mutateUInt32 t.NewFid
            let! mutationType = Gen.elements ["traversal"; "oversized"; "invalid"; "normal"]
            let! wname =
                match mutationType with
                | "traversal" -> Gen.constant [| ".."; ".."; ".."; "etc"; "passwd" |]
                | "oversized" -> Gen.constant (Array.init 100 (fun i -> sprintf "dir%d" i))
                | "invalid" -> Gen.constant [| ""; (null : string); "/"; "\x00" |]
                | _ -> Gen.constant t.Wname
            return MsgTwalk(Twalk(tag, fid, newFid, wname))
        }

    let mutateTread (t: Tread) =
        gen {
            let! tag = mutateUInt16 t.Tag
            let! fid = mutateUInt32 t.Fid
            let! offset = mutateUInt64 t.Offset
            let! count = mutateUInt32 t.Count
            return MsgTread(Tread(tag, fid, offset, count))
        }

    let mutateTwrite (t: Twrite) =
        gen {
            let! tag = mutateUInt16 t.Tag
            let! fid = mutateUInt32 t.Fid
            let! offset = mutateUInt64 t.Offset
            let! dataSize = Gen.elements [0; 1; 100000]
            let! data = Gen.arrayOfLength dataSize (Gen.choose(0, 255) |> Gen.map byte)
            return MsgTwrite(Twrite(tag, fid, offset, data))
        }

    let mutateTlcreate (t: Tlcreate) =
        gen {
            let! tag = mutateUInt16 t.Tag
            let! fid = mutateUInt32 t.Fid
            let! name = mutateString t.Name
            let! flags = mutateUInt32 t.Flags
            let! mode = mutateUInt32 t.Mode
            let! gid = mutateUInt32 t.Gid
            let size = 7u + 4u + uint32 (if isNull name then 2 else 2 + Text.Encoding.UTF8.GetByteCount(name)) + 4u + 4u + 4u
            return MsgTlcreate(Tlcreate(size, tag, fid, name, flags, mode, gid))
        }

    let mutateTsetattr (t: Tsetattr) =
        gen {
            let! tag = mutateUInt16 t.Tag
            let! fid = mutateUInt32 t.Fid
            let! valid = mutateUInt32 t.Valid
            let! mode = mutateUInt32 t.Mode
            let! uid = mutateUInt32 t.Uid
            let! gid = mutateUInt32 t.Gid
            let! fileSize = mutateUInt64 t.FileSize
            let! atimeSec = mutateUInt64 t.AtimeSec
            let! atimeNsec = mutateUInt64 t.AtimeNsec
            let! mtimeSec = mutateUInt64 t.MtimeSec
            let! mtimeNsec = mutateUInt64 t.MtimeNsec
            return MsgTsetattr(Tsetattr(t.Size, tag, fid, valid, mode, uid, gid, fileSize, atimeSec, atimeNsec, mtimeSec, mtimeNsec))
        }

    let mutateTrenameat (t: Trenameat) =
        gen {
            let! tag = mutateUInt16 t.Tag
            let! oldDirFid = mutateUInt32 t.OldDirFid
            let! oldName = mutateString t.OldName
            let! newDirFid = mutateUInt32 t.NewDirFid
            let! newName = mutateString t.NewName
            let size = 7u + 4u + uint32 (if isNull oldName then 2 else 2 + Text.Encoding.UTF8.GetByteCount(oldName)) + 4u + uint32 (if isNull newName then 2 else 2 + Text.Encoding.UTF8.GetByteCount(newName))
            return MsgTrenameat(Trenameat(size, tag, oldDirFid, oldName, newDirFid, newName))
        }

    let mutateMessage (msg: NinePMessage) =
        match msg with
        | MsgTversion t -> mutateTversion t
        | MsgTauth t -> mutateTauth t
        | MsgTwalk t -> mutateTwalk t
        | MsgTread t -> mutateTread t
        | MsgTwrite t -> mutateTwrite t
        | MsgTlcreate t -> mutateTlcreate t
        | MsgTsetattr t -> mutateTsetattr t
        | MsgTrenameat t -> mutateTrenameat t
        | _ -> Gen.constant msg // Fallback for unimplemented types

    // ─── Global Mutations ───────────────────────────────────────────────

    /// Mutate a message to boundary values
    let mutateToBoundary (msg: NinePMessage) =
        match msg with
        | MsgTversion _ -> Gen.constant (MsgTversion(Tversion(0us, 0u, "")))
        | MsgTauth _ -> Gen.constant (MsgTauth(Tauth(0us, 0u, "", "")))
        | _ -> Gen.constant msg

    /// Mutate to known invalid/malicious states
    let mutateToInvalid (msg: NinePMessage) =
        match msg with
        | MsgTversion t -> Gen.constant (MsgTversion(Tversion(t.Tag, 1u, ".."))) // MSize too small
        | _ -> Gen.constant msg

    /// Mutate a path for backend fuzzing
    let mutatePath (path: string[]) =
        gen {
            let! mutationType = Gen.elements ["traversal"; "injection"; "long"; "null"; "absolute"]
            match mutationType with
            | "traversal" -> return Array.append [| ".." |] path
            | "injection" -> return Array.append path [| "/etc/passwd" |]
            | "long" -> return Array.append path (Array.create 100 "a")
            | "null" -> return Array.append path [| (null : string) |]
            | "absolute" -> return Array.append [| "/" |] path
            | _ -> return path
        }

    // ─── Byte-Level Mutations (for raw fuzzing) ──────────────────────────

    let private rnd = Random()

    /// Flip random bits in the byte array
    let bitFlip (data: byte[]) (count: int) : byte[] =
        let mutated = Array.copy data
        for _ in 1..count do
            if mutated.Length > 0 then
                let idx = rnd.Next(mutated.Length)
                let bitPos = rnd.Next(8)
                mutated.[idx] <- mutated.[idx] ^^^ (1uy <<< bitPos)
        mutated

    /// Replace random bytes with random values
    let byteReplace (data: byte[]) (count: int) : byte[] =
        let mutated = Array.copy data
        for _ in 1..count do
            if mutated.Length > 0 then
                let idx = rnd.Next(mutated.Length)
                mutated.[idx] <- byte (rnd.Next(256))
        mutated

    /// Insert random bytes at random positions
    let byteInsert (data: byte[]) (count: int) : byte[] =
        if data.Length = 0 then Array.init count (fun _ -> byte (rnd.Next(256)))
        else
            let insertions = Array.init count (fun _ -> byte (rnd.Next(256)))
            let idx = rnd.Next(data.Length)
            Array.concat [data.[..idx-1]; insertions; data.[idx..]]

    /// Delete random bytes
    let byteDelete (data: byte[]) (count: int) : byte[] =
        if data.Length <= count then [||]
        else
            let toDelete = min count data.Length
            let idx = rnd.Next(data.Length - toDelete + 1)
            Array.append data.[..idx-1] data.[idx+toDelete..]

    /// Duplicate a random chunk of bytes
    let chunkDuplicate (data: byte[]) : byte[] =
        if data.Length < 2 then data
        else
            let chunkSize = rnd.Next(1, min 16 data.Length)
            let idx = rnd.Next(data.Length - chunkSize + 1)
            let chunk = data.[idx..idx+chunkSize-1]
            Array.concat [data.[..idx+chunkSize-1]; chunk; data.[idx+chunkSize..]]

    /// Apply random byte-level mutation
    let mutateBytes (data: byte[]) : byte[] =
        let strategies = [|
            fun d -> bitFlip d (rnd.Next(1, 5))
            fun d -> byteReplace d (rnd.Next(1, 5))
            fun d -> byteInsert d (rnd.Next(1, 10))
            fun d -> byteDelete d (rnd.Next(1, min 5 d.Length))
            chunkDuplicate
        |]
        strategies.[rnd.Next(strategies.Length)] data

    // ─── Known Attack Vectors ─────────────────────────────────────────────

    /// Generate path traversal attacks
    let generatePathTraversals () : string[][] =
        [|
            [| ".."; ".."; ".."; "etc"; "passwd" |]
            [| "."; "."; "." |]
            [| "/"; "etc"; "shadow" |]
            [| "..\\..\\..\\windows\\system32" |]
            [| String('A', 1000) |]
            [| ""; ""; "" |]
            [| "\x00"; "\x01"; "\x02" |]
            [| "admin/../../../root" |]
        |]

    /// Generate invalid FID sequences (attempting to use unattached FIDs, etc.)
    let generateInvalidFidSequence () : uint32[] =
        [| UInt32.MaxValue; 0u; UInt32.MaxValue - 1u; 0xDEADBEEFu |]

    /// Generate messages known to trigger vulnerabilities
    let generateAttackVectors () : byte[][] =
        [|
            // Integer overflow in size field (size = 0)
            [| 0x00uy; 0x00uy; 0x00uy; 0x00uy; byte MessageTypes.Tversion; 0x00uy; 0x00uy |]

            // Size field too small for header
            [| 0x03uy; 0x00uy; 0x00uy; 0x00uy; byte MessageTypes.Tversion; 0x00uy; 0x00uy |]

            // Huge size field
            [| 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy; byte MessageTypes.Tversion; 0x00uy; 0x00uy |]

            // Invalid message type
            [| 0x07uy; 0x00uy; 0x00uy; 0x00uy; 0xFFuy; 0x00uy; 0x00uy |]
        |]

    // ─── Arbitrary for FsCheck ────────────────────────────────────────────

    type MutatedMessage = MutatedMessage of NinePMessage

    type MutationArb =
        static member MutatedMessage() =
            let gen = gen {
                let! original = Generators.genNinePMessage
                let! mutated = mutateMessage original
                return MutatedMessage mutated
            }
            Arb.fromGen gen
