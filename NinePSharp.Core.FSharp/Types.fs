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
/// A Channel (Chan) is an active pointer to a resource in the namespace.
/// </summary>
type Channel =
    { Qid: Qid
      Offset: uint64
      FileSystem: INinePFileSystem
      InternalPath: string list
      IsOpened: bool }

/// <summary>
/// A Mount defines a binding between a virtual path and one or more backends.
/// </summary>
type Mount =
    { TargetPath: string list
      Backends: INinePFileSystem list
      Flags: BindFlags }

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
