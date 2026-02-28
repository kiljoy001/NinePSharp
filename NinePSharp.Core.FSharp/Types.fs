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

/// <summary>
/// Namespace lookup modes modeled after Plan 9's different name-resolution intents.
/// </summary>
type LookupMode =
    | Walk = 0
    | BindSource = 1
    | BindTarget = 2
    | Create = 3

type MountFrame =
    { MountId: uint64
      MountPath: string list
      ExitPath: string list }

type MountBranch =
    { Target: BackendTargetDescriptor
      Flags: BindFlags }

type PathState =
    { VisiblePath: string list
      MountHistory: MountFrame list }

type ChannelTarget =
    | NamespaceNode
    | BackendNode of BackendTargetDescriptor * string list

/// <summary>
/// A Channel (Chan) is an active pointer to a resource in the namespace.
/// </summary>
type Channel =
    { Qid: Qid
      Offset: uint64
      Target: ChannelTarget
      PathState: PathState
      IsOpened: bool }
    member this.InternalPath = this.PathState.VisiblePath

type MountChain =
    { MountId: uint64
      Branches: MountBranch list }
    member this.Targets = this.Branches |> List.map (fun branch -> branch.Target)
    member this.Flags =
        match this.Branches with
        | head :: _ -> head.Flags
        | [] -> BindFlags.MREPL

/// <summary>
/// A Mount defines a binding between a virtual path and one or more backend targets.
/// </summary>
type Mount =
    { TargetPath: string list
      Chain: MountChain }
    member this.Targets = this.Chain.Targets
    member this.Flags = this.Chain.Flags

/// <summary>
/// A Namespace is an immutable collection of mounts.
/// </summary>
type Namespace =
    { Mounts: Mount list }

/// <summary>
/// A Plan 9 Process represents an execution context with its own FD table.
/// </summary>
type Plan9Process =
    { Pid: int
      Namespace: Namespace
      FdTable: Map<int, Channel> }
