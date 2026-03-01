namespace NinePSharp.Core.FSharp

open System
open NinePSharp.Constants
open NinePSharp.Server.Interfaces

module NamespaceOps =

    let empty = { MountHash = Map.empty }

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

    let splitPath (path: string) =
        path.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)
        |> List.ofArray
        |> normalizeComponents

    let private stableSyntheticQidPath (kind: char) (path: string list) =
        let normalized =
            match splitPath ("/" + String.Join("/", path)) with
            | [] -> "/"
            | segments -> "/" + String.Join("/", segments)

        let key = String.Concat(kind, ":", normalized)
        let bytes = Text.Encoding.UTF8.GetBytes(key)
        let mutable hash = 14695981039346656037UL

        for b in bytes do
            hash <- (hash ^^^ uint64 b) * 1099511628211UL

        hash

    let mountQidForPath (path: string list) =
        if List.isEmpty path then
            { Type = QidType.QTDIR; Version = 0u; Path = 0UL }
        else
            { Type = QidType.QTDIR; Version = 0u; Path = stableSyntheticQidPath 'd' path }

    let mountKeyForPath (path: string list) =
        { Type = 0us
          Dev = 0u
          Qid = mountQidForPath path }

    let findMount (key: MountKey) (ns: Namespace) =
        Map.tryFind key ns.MountHash

    let mount (key: MountKey) (chain: MountChain) (ns: Namespace) =
        { MountHash = Map.add key chain ns.MountHash }

    let unmount (key: MountKey) (ns: Namespace) =
        { MountHash = Map.remove key ns.MountHash }

    let unmountByMountId (mountId: uint64) (ns: Namespace) =
        let trimBranches (branches: MountBranch list) =
            match branches with
            | [] -> []
            | [ _ ] -> []
            | head :: tail when head.Flags.HasFlag(BindFlags.MBEFORE) -> tail
            | _ -> branches |> List.rev |> List.tail |> List.rev

        { MountHash =
            ns.MountHash
            |> Map.fold (fun acc key chain ->
                if chain.MountId <> mountId then
                    Map.add key chain acc
                else
                    match trimBranches chain.Branches with
                    | [] -> acc
                    | remaining -> Map.add key { chain with Branches = remaining } acc) Map.empty }

    let private allChains (ns: Namespace) =
        ns.MountHash |> Map.values |> Seq.toList

    let private nextMountId (ns: Namespace) =
        allChains ns
        |> List.map (fun chain -> chain.MountId)
        |> List.fold max 0UL
        |> fun current -> current + 1UL

    let private tryFindByPath (path: string list) (ns: Namespace) =
        findMount (mountKeyForPath path) ns

    /// Get targets at a path (for testing/compatibility).
    /// Returns (targets, remainder) where remainder is always empty with exact-match semantics.
    let resolve (path: string list) (ns: Namespace) : BackendTargetDescriptor list * string list =
        let key = mountKeyForPath path
        match findMount key ns with
        | Some chain -> (chain.Targets, List.empty<string>)
        | None -> (List.empty<BackendTargetDescriptor>, List.empty<string>)

    let trySelectCreateTarget (path: string list) (ns: Namespace) =
        let key = mountKeyForPath path
        match findMount key ns with
        | None -> None
        | Some chain ->
            let preferred =
                chain.Branches
                |> List.tryFind (fun branch -> branch.Flags.HasFlag(BindFlags.MCREATE))

            match preferred, chain.Branches with
            | Some branch, _ -> Some branch.Target
            | None, [ branch ] -> Some branch.Target
            | _ -> None

    let bind (newPath: string) (oldPath: string) (flags: BindFlags) (ns: Namespace) =
        let targetPath = splitPath oldPath
        let sourcePath = splitPath newPath
        let sourceKey = mountKeyForPath sourcePath
        let sourceTargets =
            match findMount sourceKey ns with
            | Some chain -> chain.Targets
            | None -> []
        let sourceBranches = sourceTargets |> List.map (fun target -> { Target = target; Flags = flags })
        let targetKey = mountKeyForPath targetPath
        let existingMount = tryFindByPath targetPath ns

        let updatedChain =
            match existingMount with
            | Some chain when flags.HasFlag(BindFlags.MBEFORE) ->
                { chain with Branches = sourceBranches @ chain.Branches }
            | Some chain when flags.HasFlag(BindFlags.MAFTER) ->
                { chain with Branches = chain.Branches @ sourceBranches }
            | Some chain ->
                { chain with Branches = sourceBranches }
            | None ->
                { MountId = nextMountId ns
                  From = targetKey
                  MountPath = targetPath
                  Branches = sourceBranches }

        mount targetKey updatedChain ns
