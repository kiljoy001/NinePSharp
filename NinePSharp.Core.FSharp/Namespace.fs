namespace NinePSharp.Core.FSharp

open System
open NinePSharp.Server.Interfaces

type NamespaceResolution =
    { LookupMode: LookupMode
      NormalizedPath: string list
      MatchedMount: Mount option
      Branches: MountBranch list
      Targets: BackendTargetDescriptor list
      Remainder: string list }

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

    let private nextMountId (ns: Namespace) =
        ns.Mounts
        |> List.map (fun mount -> mount.Chain.MountId)
        |> List.fold max 0UL
        |> fun current -> current + 1UL

    let private createBranches flags targets =
        targets |> List.map (fun target -> { Target = target; Flags = flags })

    let private createChain mountId branches =
        { MountId = mountId
          Branches = branches }

    let private findMatchingMount (mode: LookupMode) (normalizedPath: string list) (ns: Namespace) =
        let matches =
            match mode with
            | LookupMode.BindTarget ->
                ns.Mounts
                |> List.filter (fun mount -> mount.TargetPath = normalizedPath)
            | _ ->
                ns.Mounts
                |> List.filter (fun mount ->
                    if mount.TargetPath.Length > normalizedPath.Length then
                        false
                    else
                        List.take mount.TargetPath.Length normalizedPath = mount.TargetPath)

        matches
        |> List.sortByDescending (fun mount -> mount.TargetPath.Length)
        |> List.tryHead

    /// <summary>
    /// Normalizes a path string into a list of components.
    /// </summary>
    let splitPath (path: string) =
        path.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)
        |> List.ofArray
        |> normalizeComponents

    /// <summary>
    /// Resolves a path within the namespace using an explicit lookup mode.
    /// </summary>
    let resolveWithMode (mode: LookupMode) (path: string list) (ns: Namespace) =
        let normalizedPath = normalizeComponents path

        match findMatchingMount mode normalizedPath ns with
        | Some mount ->
            let remainder =
                match mode with
                | LookupMode.BindTarget -> []
                | _ -> normalizedPath |> List.skip mount.TargetPath.Length

            let branches = mount.Chain.Branches
            { LookupMode = mode
              NormalizedPath = normalizedPath
              MatchedMount = Some mount
              Branches = branches
              Targets = mount.Targets
              Remainder = remainder }
        | None ->
            { LookupMode = mode
              NormalizedPath = normalizedPath
              MatchedMount = None
              Branches = []
              Targets = []
              Remainder = normalizedPath }

    /// <summary>
    /// Resolves a path within the namespace to a list of potential backends (for union mounts).
    /// </summary>
    let resolve (path: string list) (ns: Namespace) =
        let resolution = resolveWithMode LookupMode.Walk path ns
        (resolution.Targets, resolution.Remainder)

    let trySelectCreateTarget (path: string list) (ns: Namespace) =
        let resolution = resolveWithMode LookupMode.Create path ns

        match resolution.MatchedMount with
        | None -> None
        | Some _ ->
            let preferred =
                resolution.Branches
                |> List.tryFind (fun branch -> branch.Flags.HasFlag(BindFlags.MCREATE))

            match preferred, resolution.Branches with
            | Some branch, _ -> Some branch.Target
            | None, [ branch ] -> Some branch.Target
            | _ -> None

    /// <summary>
    /// Performs a bind operation, returning a new immutable Namespace.
    /// </summary>
    let bind (newPath: string) (oldPath: string) (flags: BindFlags) (ns: Namespace) =
        let targetPath = splitPath oldPath
        let sourceResolution = resolveWithMode LookupMode.BindSource (splitPath newPath) ns
        let sourceTargets = sourceResolution.Targets
        let sourceBranches = createBranches flags sourceTargets
        let existingMount = ns.Mounts |> List.tryFind (fun mount -> mount.TargetPath = targetPath)

        match existingMount with
        | Some mount when flags.HasFlag(BindFlags.MBEFORE) ->
            let updated =
                { mount with
                    Chain =
                        { mount.Chain with
                            Branches = sourceBranches @ mount.Chain.Branches } }

            { ns with
                Mounts =
                    ns.Mounts
                    |> List.map (fun current -> if current.TargetPath = targetPath then updated else current) }

        | Some mount when flags.HasFlag(BindFlags.MAFTER) ->
            let updated =
                { mount with
                    Chain =
                        { mount.Chain with
                            Branches = mount.Chain.Branches @ sourceBranches } }

            { ns with
                Mounts =
                    ns.Mounts
                    |> List.map (fun current -> if current.TargetPath = targetPath then updated else current) }

        | Some mount ->
            let updated =
                { mount with
                    Chain = createChain mount.Chain.MountId sourceBranches }

            { ns with
                Mounts =
                    ns.Mounts
                    |> List.map (fun current -> if current.TargetPath = targetPath then updated else current) }

        | None ->
            let newMount =
                { TargetPath = targetPath
                  Chain = createChain (nextMountId ns) sourceBranches }

            { ns with
                Mounts = newMount :: ns.Mounts }
