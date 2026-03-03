namespace NinePSharp.Core.FSharp

open System
open NinePSharp.Constants
open NinePSharp.Server.Interfaces

/// <summary>
/// Standard 9P QID structure (Type, Version, Path).
/// </summary>
type Qid = 
    { Type: QidType
      Version: uint32
      Path: uint64 }

/// <summary>
/// Plan 9 Bind flags.
/// </summary>
[<Flags>]
type BindFlags =
    | MREPL   = 0x0000  // Replace existing mount
    | MBEFORE = 0x0001  // Union before existing mount
    | MAFTER  = 0x0002  // Union after existing mount
    | MCREATE = 0x0004  // Allow creation in this mount

type MountBranch =
    { Target: string list
      Flags: BindFlags }

/// <summary>
/// Mount key for channel identity lookup (like 9front's type/dev/qid triple).
/// </summary>
type MountKey =
    { Type: uint16
      Dev: uint32           // 32-bit like 9front's ulong
      Qid: Qid }

type ChannelTarget =
    | NamespaceNode
    | BackendNode of string list

/// <summary>
/// Path state like 9front's Path structure.
/// Mtpt is mount point history (like Chan **mtpt in 9front).
/// </summary>
type PathState =
    { VisiblePath: string list
      Mtpt: Channel list }      // mount point history (like 9front's Path.mtpt[])
/// <summary>
/// A Channel (Chan) is an active pointer to a resource in the namespace.
/// Like 9front's Chan structure with type/dev/qid identity.
/// </summary>
and Channel =
    { Type: uint16            // device type
      Dev: uint32             // device instance (32-bit like 9front's ulong)
      Qid: Qid
      Offset: uint64
      Target: ChannelTarget
      PathState: PathState
      IsOpened: bool
      Umh: MountChain option  // mount head for union reads
      Umc: Channel option     // current chan in union iteration
      Uri: int                // union read index
      Cname: (uint16 * uint32 * Qid) list } // canonical name (sequence of Type, Dev, Qid)
    member this.InternalPath = this.PathState.VisiblePath
and MountChain =
    { MountId: uint64
      From: MountKey
      MountPath: string list
      Branches: MountBranch list }
    member this.Targets = this.Branches |> List.map (fun branch -> branch.Target)
    member this.Flags =
        match this.Branches with
        | head :: _ -> head.Flags
        | [] -> BindFlags.MREPL

module MountKeyModule =
    let fromChannel (channel: Channel) =
        { Type = channel.Type
          Dev = channel.Dev
          Qid = channel.Qid }

/// <summary>
/// A Namespace is an immutable collection of mounts.
/// </summary>
type Namespace =
    { MountHash: Map<MountKey, MountChain> }

/// <summary>
/// A Plan 9 Process represents an execution context with its own FD table.
/// </summary>
type Plan9Process =
    { Pid: int
      Namespace: Namespace
      Dot: Channel
      Slash: Channel
      FdTable: Map<int, Channel> }
