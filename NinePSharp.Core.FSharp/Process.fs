namespace NinePSharp.Core.FSharp

module Process =
    let create pid ns =
        { Pid = pid
          Namespace = ns
          FdTable = Map.empty }

    let fork newPid (parent: Plan9Process) =
        { parent with
            Pid = newPid
            Namespace = { Mounts = parent.Namespace.Mounts |> List.ofSeq }
            FdTable = parent.FdTable }

    let bind newPath oldPath flags (proc: Plan9Process) =
        { proc with Namespace = NamespaceOps.bind newPath oldPath flags proc.Namespace }

    let addFd fd channel (proc: Plan9Process) =
        { proc with FdTable = proc.FdTable |> Map.add fd channel }

    let tryGetChannel fd (proc: Plan9Process) =
        proc.FdTable |> Map.tryFind fd
