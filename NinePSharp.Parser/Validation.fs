namespace NinePSharp.Parser

open System.Text
open NinePSharp.Messages
open NinePSharp.Constants

module Validation =

    let MaxStringLength = 65535 // 9P strings use 16-bit length
    let MaxWalkComponents = 16

    let private andThen next result =
        match result with
        | Error e -> Error e
        | Ok () -> next ()

    let private validateString fieldName (s: string) : Result<unit, string> =
        if isNull s || Encoding.UTF8.GetByteCount(s) > MaxStringLength then
            Error $"Invalid {fieldName}"
        else
            Ok ()

    let private validateOptionalString fieldName (s: string) : Result<unit, string> =
        if isNull s then
            Ok ()
        else
            validateString fieldName s

    let private validateStrings fields : Result<unit, string> =
        fields
        |> List.fold
            (fun result (fieldName, value) ->
                result |> andThen (fun () -> validateString fieldName value))
            (Ok ())

    let private validateMSize (msize: uint32) : Result<unit, string> =
        if msize < uint32 NinePConstants.HeaderSize then
            Error "MSize too small"
        else
            Ok ()

    let private validateWalkComponents (wnames: string[]) : Result<unit, string> =
        if isNull wnames then
            Error "Invalid walk components"
        elif wnames.Length > MaxWalkComponents then
            Error "Too many walk components"
        else
            wnames
            |> Array.tryFind (fun n -> isNull n || Encoding.UTF8.GetByteCount(n) > MaxStringLength)
            |> function
                | Some _ -> Error "Invalid walk component string"
                | None -> Ok ()

    let private validateWalkQids (wqids: Qid[]) : Result<unit, string> =
        if isNull wqids then
            Error "Invalid walk qids"
        elif wqids.Length > MaxWalkComponents then
            Error "Too many walk qids"
        else
            Ok ()

    let private validateStat fieldName (stat: Stat) : Result<unit, string> =
        validateStrings
            [ $"{fieldName}.name", stat.Name
              $"{fieldName}.uid", stat.Uid
              $"{fieldName}.gid", stat.Gid
              $"{fieldName}.muid", stat.Muid ]
        |> andThen (fun () -> validateOptionalString $"{fieldName}.extension" stat.Extension)

    let private validateTversion (t: Tversion) : Result<unit, string> =
        validateMSize t.MSize
        |> andThen (fun () -> validateString "version string" t.Version)

    let private validateRversion (r: Rversion) : Result<unit, string> =
        validateMSize r.MSize
        |> andThen (fun () -> validateString "version string" r.Version)

    let private validateTauth (t: Tauth) : Result<unit, string> =
        validateStrings [ "uname", t.Uname; "aname", t.Aname ]

    let private validateTattach (t: Tattach) : Result<unit, string> =
        validateStrings [ "uname", t.Uname; "aname", t.Aname ]

    let private validateTwalk (t: Twalk) : Result<unit, string> =
        validateWalkComponents t.Wname

    let validate (msg: NinePMessage) : Result<unit, string> =
        match msg with
        | MsgTversion t -> validateTversion t
        | MsgRversion r -> validateRversion r
        | MsgTauth t -> validateTauth t
        | MsgTattach t -> validateTattach t
        | MsgRerror r -> validateString "error string" r.Ename
        | MsgTcreate t -> validateString "name" t.Name
        | MsgTwstat t -> validateStat "stat" t.Stat
        | MsgRstat r -> validateStat "stat" r.Stat
        | MsgTwalk t -> validateTwalk t
        | MsgRwalk r -> validateWalkQids r.Wqid
        | MsgTlcreate t -> validateString "name" t.Name
        | MsgTsymlink t -> validateStrings [ "name", t.Name; "symtgt", t.Symtgt ]
        | MsgTmknod t -> validateString "name" t.Name
        | MsgTrename t -> validateString "name" t.Name
        | MsgTreadlink _ -> Ok ()
        | MsgRreadlink r -> validateString "target" r.Target
        | MsgTxattrwalk t -> validateString "name" t.Name
        | MsgTxattrcreate t -> validateString "name" t.Name
        | MsgTlock t -> validateString "clientId" t.ClientId
        | MsgTgetlock t -> validateString "clientId" t.ClientId
        | MsgRgetlock r -> validateString "clientId" r.ClientId
        | MsgTlink t -> validateString "name" t.Name
        | MsgTmkdir t -> validateString "name" t.Name
        | MsgTrenameat t -> validateStrings [ "old name", t.OldName; "new name", t.NewName ]
        | MsgTunlinkat t -> validateString "name" t.Name
        | _ -> Ok ()
