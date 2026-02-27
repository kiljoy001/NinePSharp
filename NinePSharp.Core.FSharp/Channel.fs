namespace NinePSharp.Core.FSharp

module ChannelOps =
    let private applySegment (path: string list) (segment: string) =
        match segment with
        | "" -> path
        | "." -> path
        | ".." ->
            match path with
            | [] -> []
            | _ -> path |> List.take (path.Length - 1)
        | value -> path @ [ value ]

    /// <summary>
    /// Walk returns a new immutable channel rooted at the updated internal path.
    /// </summary>
    let walk (segments: string list) (channel: Channel) =
        let nextPath = segments |> List.fold applySegment channel.InternalPath
        { channel with InternalPath = nextPath; Offset = 0UL; IsOpened = false }
