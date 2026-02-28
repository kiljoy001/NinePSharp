namespace NinePSharp.Core.FSharp

module ChannelOps =
    let createPathState (visiblePath: seq<string>) =
        { VisiblePath = visiblePath |> List.ofSeq
          MountHistory = [] }

    let createPathStateWithHistory (visiblePath: seq<string>) (history: seq<MountFrame>) =
        { VisiblePath = visiblePath |> List.ofSeq
          MountHistory = history |> List.ofSeq }

    let private parentPath (path: string list) =
        match path with
        | [] -> []
        | _ -> path |> List.take (path.Length - 1)

    let createNamespaceNode qid visiblePath =
        { Qid = qid
          Offset = 0UL
          Target = NamespaceNode
          PathState = createPathState visiblePath
          IsOpened = false }

    let createNamespaceNodeWithPathState qid pathState =
        { Qid = qid
          Offset = 0UL
          Target = NamespaceNode
          PathState = pathState
          IsOpened = false }

    let createBackendNode qid target relativePath visiblePath =
        { Qid = qid
          Offset = 0UL
          Target = BackendNode(target, relativePath |> List.ofSeq)
          PathState = createPathState visiblePath
          IsOpened = false }

    let createBackendNodeWithPathState qid target relativePath pathState =
        { Qid = qid
          Offset = 0UL
          Target = BackendNode(target, relativePath |> List.ofSeq)
          PathState = pathState
          IsOpened = false }

    let private applySegment (target: ChannelTarget) (pathState: PathState) (segment: string) =
        match segment with
        | "" -> pathState
        | "." -> pathState
        | ".." ->
            match target, pathState.MountHistory with
            | BackendNode(_, relativePath), frame :: rest when List.isEmpty relativePath ->
                { VisiblePath = frame.ExitPath
                  MountHistory = rest }
            | _ ->
                { pathState with VisiblePath = parentPath pathState.VisiblePath }
        | value ->
            { pathState with VisiblePath = pathState.VisiblePath @ [ value ] }

    /// <summary>
    /// Walk returns a new immutable channel rooted at the updated internal path.
    /// </summary>
    let walk (segments: string list) (channel: Channel) =
        let nextPathState = segments |> List.fold (applySegment channel.Target) channel.PathState
        { channel with PathState = nextPathState; Offset = 0UL; IsOpened = false }
