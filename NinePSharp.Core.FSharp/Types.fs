namespace NinePSharp.Core.FSharp

open NinePSharp.Constants

/// <summary>
/// Standard 9P QID structure (Type, Version, Path).
/// </summary>
type Qid = 
    { Type: QidType
      Version: uint32
      Path: uint64 }

type ChannelTarget =
    | BackendNode of string list

type Channel =
    { Qid: Qid
      Offset: uint64
      Target: ChannelTarget
      InternalPath: string list
      IsOpened: bool
    }
