namespace NinePSharp.Core.FSharp

open System.Text
open NinePSharp.Constants
open NinePSharp.Server.Interfaces

module ChannelOps =
    let createPathState (visiblePath: seq<string>) =
        { VisiblePath = visiblePath |> List.ofSeq
          Mtpt = [] }

    let createPathStateWithMtpt (visiblePath: seq<string>) (mtpt: seq<Channel>) =
        { VisiblePath = visiblePath |> List.ofSeq
          Mtpt = mtpt |> List.ofSeq }

    let private parentPath (path: string list) =
        match path with
        | [] -> []
        | _ -> path |> List.take (path.Length - 1)

    /// Generate stable Type/Dev for namespace nodes to match mountKeyForPath
    let private namespaceTypeDevForPath (path: string list) =
        let typeValue = uint16 '#'
        let devValue =
            if List.isEmpty path then 0u
            else uint32 (int64 (PathHash.stableHash 'D' path) &&& 0xFFFFFFFFL)
        (typeValue, devValue)

    let createNamespaceNode qid visiblePath =
        let path = visiblePath |> List.ofSeq
        let (typeValue, devValue) = namespaceTypeDevForPath path
        { Type = typeValue
          Dev = devValue
          Qid = qid
          Offset = 0UL
          Target = NamespaceNode
          PathState = createPathState visiblePath
          IsOpened = false
          Umh = None
          Umc = None
          Uri = 0 }

    let createNamespaceNodeWithPathState qid pathState =
        let (typeValue, devValue) = namespaceTypeDevForPath pathState.VisiblePath
        { Type = typeValue
          Dev = devValue
          Qid = qid
          Offset = 0UL
          Target = NamespaceNode
          PathState = pathState
          IsOpened = false
          Umh = None
          Umc = None
          Uri = 0 }

    let createBackendNode qid (target: BackendTargetDescriptor) relativePath visiblePath =
        { Type = 1us
          Dev = uint32 (hash (target.Id, target.MountPath) &&& System.Int32.MaxValue)
          Qid = qid
          Offset = 0UL
          Target = BackendNode(target, relativePath |> List.ofSeq)
          PathState = createPathState visiblePath
          IsOpened = false
          Umh = None
          Umc = None
          Uri = 0 }

    let createBackendNodeWithPathState qid (target: BackendTargetDescriptor) relativePath pathState =
        { Type = 1us
          Dev = uint32 (hash (target.Id, target.MountPath) &&& System.Int32.MaxValue)
          Qid = qid
          Offset = 0UL
          Target = BackendNode(target, relativePath |> List.ofSeq)
          PathState = pathState
          IsOpened = false
          Umh = None
          Umc = None
          Uri = 0 }

    /// Result of applying a segment - may cross mount boundary
    type private WalkStep =
        { Target: ChannelTarget
          PathState: PathState }

    let private applySegmentFull (step: WalkStep) (segment: string) : WalkStep =
        match segment with
        | "" -> step
        | "." -> step
        | ".." ->
            match step.Target, step.PathState.Mtpt with
            | BackendNode(_, relativePath), prevChan :: rest when List.isEmpty relativePath ->
                // Crossed a mount going down, now going back up - restore previous chan entirely
                { Target = prevChan.Target
                  PathState = { VisiblePath = prevChan.PathState.VisiblePath; Mtpt = rest } }
            | BackendNode(target, relativePath), _ when not (List.isEmpty relativePath) ->
                // Still inside backend, walk up within backend
                { step with PathState = { step.PathState with VisiblePath = parentPath step.PathState.VisiblePath } }
            | _ ->
                // Namespace node or at namespace root
                { step with PathState = { step.PathState with VisiblePath = parentPath step.PathState.VisiblePath } }
        | value ->
            { step with PathState = { step.PathState with VisiblePath = step.PathState.VisiblePath @ [ value ] } }

    /// <summary>
    /// Walk returns a new immutable channel rooted at the updated internal path.
    /// Handles mount boundary crossing with ".." by restoring previous channel's target.
    /// Updates Type/Dev/Qid for namespace nodes to match path (channel identity semantics).
    /// </summary>
    let walk (segments: string list) (channel: Channel) =
        let initial = { Target = channel.Target; PathState = channel.PathState }
        let final = segments |> List.fold applySegmentFull initial
        // Update Type/Dev/Qid for namespace nodes to maintain channel identity
        let (newType, newDev, newQid) =
            match final.Target with
            | NamespaceNode ->
                // Recompute Type/Dev/Qid to match new path (matches mountKeyForPath logic)
                let path = final.PathState.VisiblePath
                let (typeVal, devVal) = namespaceTypeDevForPath path
                let qidVal =
                    if List.isEmpty path then
                        { Type = QidType.QTDIR; Version = 0u; Path = 0UL }
                    else
                        { Type = QidType.QTDIR; Version = 0u; Path = PathHash.stableHash 'd' path }
                (typeVal, devVal, qidVal)
            | BackendNode _ ->
                // Backend walks update identity via backend response
                (channel.Type, channel.Dev, channel.Qid)
        { channel with
            Type = newType
            Dev = newDev
            Qid = newQid
            Target = final.Target
            PathState = final.PathState
            Offset = 0UL
            IsOpened = false
            Umc = None
            Uri = 0 }
