namespace NinePSharp.Parser

open NinePSharp.Messages
open NinePSharp.Constants

module Validation =
    
    let MaxStringLength = 65535 // 9P strings use 16-bit length
    
    let validate (msg: NinePMessage) : Result<unit, string> =
        match msg with
        | MsgTversion t ->
            if t.MSize < uint32 NinePConstants.HeaderSize then
                Error "MSize too small"
            else if isNull t.Version || t.Version.Length > MaxStringLength then
                Error "Invalid version string"
            else
                Ok ()
        | MsgTauth t ->
            if isNull t.Uname || t.Uname.Length > MaxStringLength then
                Error "Invalid uname"
            else if isNull t.Aname || t.Aname.Length > MaxStringLength then
                Error "Invalid aname"
            else
                Ok ()
        | MsgTattach t ->
            if isNull t.Uname || t.Uname.Length > MaxStringLength then
                Error "Invalid uname"
            else if isNull t.Aname || t.Aname.Length > MaxStringLength then
                Error "Invalid aname"
            else
                Ok ()
        | MsgTwalk t ->
            if t.Wname = null then
                Error "Wname array is null"
            else if t.Wname.Length > 16 then
                Error "Too many walk components"
            else if t.Wname |> Array.exists (fun n -> n = null || n.Length > MaxStringLength) then
                Error "Invalid walk component string"
            else
                Ok ()
        | _ -> Ok () // Basic validation for others
