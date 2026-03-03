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

    let mountQidForPath (path: string list) =
        if List.isEmpty path then
            { Type = QidType.QTDIR; Version = 0u; Path = 0UL }
        else
            { Type = QidType.QTDIR; Version = 0u; Path = PathHash.stableHash 'd' path }

    /// Path-based mount key for initialization (buildRootNamespace).
    /// Generates stable Type/Dev from path for the synthetic boot namespace.
    /// Runtime bind operations should use mountByChannel with real channel identity.
    let mountKeyForPathInit (path: string list) =
        // Use '#' (0x23) as Type for namespace-synthetic channels
        let typeValue = uint16 '#'
        // Generate stable Dev from path hash (9front uses device instance numbers)
        let devValue =
            if List.isEmpty path then 0u
            else uint32 (int64 (PathHash.stableHash 'D' path) &&& 0xFFFFFFFFL)
        { Type = typeValue
          Dev = devValue
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

    let private tryFindByPathInit (path: string list) (ns: Namespace) =
        findMount (mountKeyForPathInit path) ns

    /// Get targets at a path (for testing/compatibility).
    /// Returns (targets, remainder) where remainder is always empty with exact-match semantics.
    let resolve (path: string list) (ns: Namespace) : string list list * string list =
        let key = mountKeyForPathInit path
        match findMount key ns with
        | Some chain -> (chain.Targets, List.empty<string>)
        | None -> (List.empty<string list>, List.empty<string>)

    let trySelectCreateTarget (path: string list) (ns: Namespace) =
        let key = mountKeyForPathInit path
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

    /// Select create target for a mount using channel-based key (9front semantics).
    /// Prefers MCREATE branch, falls back to only branch if singular.
    let trySelectCreateTargetByKey (key: MountKey) (ns: Namespace) =
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

    /// Mount using channel identity (9front lexnames semantics).
    /// Both target and source channels must have been walked to resolve their identity.
    let mountByChannel (targetChan: Channel) (sourceChan: Channel) (flags: BindFlags) (ns: Namespace) =
        let targetKey = MountKeyModule.fromChannel targetChan
        let sourceKey = MountKeyModule.fromChannel sourceChan

        // Get source branches (what we're mounting)
        let sourceBranches =
            match findMount sourceKey ns with
            | Some chain -> chain.Branches
            | None ->
                // Source not mounted - use direct target from channel
                match sourceChan.Target with
                | BackendNode(targetPath) -> [ { Target = targetPath; Flags = flags } ]
                | NamespaceNode -> [] // Mounting a virtual dir as-is?

        let existingMount = findMount targetKey ns
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
                  MountPath = targetChan.InternalPath
                  Branches = sourceBranches }

        mount targetKey updatedChain ns

    let bind (targetChan: Channel) (sourceChan: Channel) (flags: BindFlags) (ns: Namespace) =
        mountByChannel targetChan sourceChan flags ns
