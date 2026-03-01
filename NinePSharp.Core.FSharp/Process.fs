namespace NinePSharp.Core.FSharp

module Process =
    let create pid ns rootChannel =
        { Pid = pid
          Namespace = ns
          Dot = rootChannel
          Slash = rootChannel
          FdTable = Map.empty }

    let fork newPid (parent: Plan9Process) =
        { parent with
            Pid = newPid
            Namespace = { MountHash = parent.Namespace.MountHash }
            Dot = parent.Dot
            Slash = parent.Slash
            FdTable = parent.FdTable }

    let chdir newDot (proc: Plan9Process) =
        { proc with Dot = newDot }

    let bind newPath oldPath flags (proc: Plan9Process) =
        { proc with Namespace = NamespaceOps.bind newPath oldPath flags proc.Namespace }

    let unmount key (proc: Plan9Process) =
        { proc with Namespace = NamespaceOps.unmount key proc.Namespace }

    let addFd fd channel (proc: Plan9Process) =
        { proc with FdTable = proc.FdTable |> Map.add fd channel }

    let tryGetChannel fd (proc: Plan9Process) =
        proc.FdTable |> Map.tryFind fd
