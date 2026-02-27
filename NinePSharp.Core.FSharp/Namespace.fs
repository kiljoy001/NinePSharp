namespace NinePSharp.Core.FSharp

open System
open NinePSharp.Server.Interfaces
open NinePSharp.Messages

module NamespaceOps =

    /// <summary>
    /// Initial empty namespace.
    /// </summary>
    let empty = { Mounts = [] }

    let private normalizeComponents (components: string list) =
        let folder (acc: string list) (segment: string) =
            match segment with
            | "" -> acc
            | "." -> acc
            | ".." ->
                match acc with
                | [] -> []
                | _ -> acc |> List.take (acc.Length - 1)
            | _ -> acc @ [ segment ]

        components |> List.fold folder []

    /// <summary>
    /// Normalizes a path string into a list of components.
    /// </summary>
    let splitPath (path: string) =
        path.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)
        |> List.ofArray
        |> normalizeComponents

    /// <summary>
    /// Resolves a path within the namespace to a list of potential backends (for union mounts).
    /// </summary>
    let resolve (path: string list) (ns: Namespace) =
        let normalizedPath = normalizeComponents path
        // Longest-prefix match logic
        ns.Mounts
        |> List.filter (fun m ->
            if m.TargetPath.Length > normalizedPath.Length then false
            else List.take m.TargetPath.Length normalizedPath = m.TargetPath)
        |> List.sortByDescending (fun m -> m.TargetPath.Length)
        |> List.tryHead
        |> function
           | Some m -> (m.Backends, List.skip m.TargetPath.Length normalizedPath)
           | None -> ([], normalizedPath)

    /// <summary>
    /// Performs a bind operation, returning a new immutable Namespace.
    /// </summary>
    let bind (newPath: string) (oldPath: string) (flags: BindFlags) (ns: Namespace) =
        let target = splitPath oldPath
        let sourcePath = splitPath newPath
        let sourceBackends, _ = resolve sourcePath ns
        let existingMount = ns.Mounts |> List.tryFind (fun m -> m.TargetPath = target)

        match existingMount with
        | Some m when flags.HasFlag(BindFlags.MBEFORE) ->
            // Union before: prepend to the backend list
            let updated = { m with Backends = sourceBackends @ m.Backends; Flags = flags }
            { ns with Mounts = ns.Mounts |> List.map (fun x -> if x.TargetPath = target then updated else x) }

        | Some m when flags.HasFlag(BindFlags.MAFTER) ->
            // Union after: append to the backend list
            let updated = { m with Backends = m.Backends @ sourceBackends; Flags = flags }
            { ns with Mounts = ns.Mounts |> List.map (fun x -> if x.TargetPath = target then updated else x) }

        | _ ->
            // Replace or New mount
            let newMount = { TargetPath = target; Backends = sourceBackends; Flags = flags }
            { ns with Mounts = newMount :: (ns.Mounts |> List.filter (fun m -> m.TargetPath <> target)) }
