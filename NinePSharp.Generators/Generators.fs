namespace NinePSharp.Generators

open FsCheck
open NinePSharp.Constants
open NinePSharp.Messages

module Generators =
    // Wrapper so we don't accidentally get null strings which would throw in C# UTF8.GetByteCount
    type Valid9PString = 
        { Value: string }

    type NinePGenerators =
        static member Valid9PStrings() =
            Arb.Default.String().Generator
            |> Gen.map (fun s -> { Value = if s = null then "" else s })
            |> Arb.fromGen

        static member Qids() =
            gen {
            let! qType = Gen.elements [
                QidType.QTDIR
                QidType.QTAPPEND
                QidType.QTEXCL
                QidType.QTMOUNT
                QidType.QTAUTH
                QidType.QTTMP
                QidType.QTFILE
            ]
            let! version = Arb.generate<uint32>
            let! path = Arb.generate<uint64>
            return Qid(qType, version, path)
        }
        |> Arb.fromGen
