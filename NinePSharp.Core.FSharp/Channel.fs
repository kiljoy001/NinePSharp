namespace NinePSharp.Core.FSharp

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

    let createNamespaceNode qid visiblePath =
        { Type = 0us
          Dev = 0u
          Qid = qid
          Offset = 0UL
          Target = NamespaceNode
          PathState = createPathState visiblePath
          IsOpened = false
          Umh = None
          Umc = None
          Uri = 0 }

    let createNamespaceNodeWithPathState qid pathState =
        { Type = 0us
          Dev = 0u
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
    /// </summary>
    let walk (segments: string list) (channel: Channel) =
        let initial = { Target = channel.Target; PathState = channel.PathState }
        let final = segments |> List.fold applySegmentFull initial
        { channel with
            Target = final.Target
            PathState = final.PathState
            Offset = 0UL
            IsOpened = false
            Umc = None
            Uri = 0 }
