namespace NinePSharp.Server.FSharp

open System
open System.Buffers
open System.Collections.Concurrent
open System.Security
open System.Security.Cryptography.X509Certificates
open System.Text
open System.Threading
open System.Threading.Tasks
open NinePSharp.Constants
open NinePSharp.Core.FSharp
open NinePSharp.Messages
open NinePSharp.Parser
open NinePSharp.Server
open NinePSharp.Server.Interfaces
open NinePSharp.Server.Utils

type private SessionBox(state: ProtocolSession) =
    let gate = obj()
    member _.Gate = gate
    member val State = state with get, set

type NinePFSDispatcherEngine(attachResolver: IAttachResolver) =
    let defaultSessionId = "__compat__"
    let fids = ConcurrentDictionary<uint32, INinePFileSystem>()
    let authFids = ConcurrentDictionary<uint32, SecureString>()
    let sessions = ConcurrentDictionary<string, SessionBox>(StringComparer.Ordinal)
    let fidOperationGates = ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal)

    let withLock (gate: obj) (action: unit -> 'T) : 'T =
        lock gate action

    let createVirtualBinding dialect (visiblePath: string list) (qidType: QidType) (qidVersion: uint32) (qidPath: uint64) =
        let _ = dialect
        ProtocolSessionOps.createBinding NamespaceNode qidType qidVersion qidPath visiblePath

    let createVirtualBindingWithPathState dialect (pathState: PathState) (qidType: QidType) (qidVersion: uint32) (qidPath: uint64) =
        let _ = dialect
        ProtocolSessionOps.createBindingWithPathState NamespaceNode qidType qidVersion qidPath pathState

    let createBackendBinding (target: BackendTargetDescriptor) (relativePath: string list) (visiblePath: string list) (qidType: QidType) (qidVersion: uint32) (qidPath: uint64) =
        ProtocolSessionOps.createBinding (BackendNode(target, relativePath)) qidType qidVersion qidPath visiblePath

    let createBackendBindingWithPathState (target: BackendTargetDescriptor) (relativePath: string list) (pathState: PathState) (qidType: QidType) (qidVersion: uint32) (qidPath: uint64) =
        ProtocolSessionOps.createBindingWithPathState (BackendNode(target, relativePath)) qidType qidVersion qidPath pathState

    let stableSyntheticQidPath (kind: char) (path: string list) =
        let normalized =
            match NamespaceOps.splitPath ("/" + String.Join("/", path)) with
            | [] -> "/"
            | segments -> "/" + String.Join("/", segments)

        let key = String.Concat(kind, ":", normalized)
        let bytes = Encoding.UTF8.GetBytes(key)
        let mutable hash = 14695981039346656037UL

        for b in bytes do
            hash <- (hash ^^^ uint64 b) * 1099511628211UL

        hash

    let syntheticDirectoryQid (path: string list) =
        if List.isEmpty path then
            NinePSharp.Constants.Qid(QidType.QTDIR, 0u, 0UL)
        else
            NinePSharp.Constants.Qid(QidType.QTDIR, 0u, stableSyntheticQidPath 'd' path)

    let syntheticFileQid (path: string list) =
        NinePSharp.Constants.Qid(QidType.QTFILE, 0u, stableSyntheticQidPath 'f' path)

    let hasPrefix (path: string list) (prefix: string list) =
        if prefix.Length > path.Length then
            false
        else
            List.forall2 (=) prefix (path |> List.take prefix.Length)

    let parentPath (path: string list) =
        match path with
        | [] -> []
        | _ -> path |> List.take (path.Length - 1)

    let normalizePath (basePath: string list) (segments: string seq) =
        String.Join("/", Seq.concat [ basePath :> seq<string>; segments ])
        |> fun combined -> "/" + combined
        |> NamespaceOps.splitPath

    let buildRootNamespace (certificate: X509Certificate2) =
        let mounts =
            attachResolver.GetRootMounts(certificate)
            |> Seq.mapi (fun index mount ->
                if String.IsNullOrWhiteSpace(mount.MountPath) then
                    None
                else
                    Some(struct (index, mount)))
            |> Seq.choose id
            |> Seq.groupBy (fun struct (_, mount) -> NamespaceOps.splitPath mount.MountPath)
            |> Seq.map (fun (targetPath, group) ->
                let ordered = group |> Seq.sortBy (fun struct (index, _) -> index) |> Seq.toList
                { TargetPath = targetPath
                  Chain =
                      { MountId = uint64 ((ordered |> List.head |> fun struct (index, _) -> index) + 1)
                        Branches =
                            ordered
                            |> List.map (fun struct (_, mount) ->
                                { Target = mount.Target
                                  Flags = BindFlags.MAFTER }) } })
            |> Seq.toList

        { Mounts = mounts }

    let createErrorResponse tag dialect (error: NinePProtocolException) : obj =
        if dialect = NinePDialect.NineP2000L then
            Rlerror(tag, uint32 error.ErrorCode) :> obj
        else
            Rerror(tag, error.ErrorMessage) :> obj

    let getTag message =
        match message with
        | NinePMessage.MsgTversion t -> t.Tag
        | NinePMessage.MsgTauth t -> t.Tag
        | NinePMessage.MsgTattach t -> t.Tag
        | NinePMessage.MsgTwalk t -> t.Tag
        | NinePMessage.MsgTopen t -> t.Tag
        | NinePMessage.MsgTread t -> t.Tag
        | NinePMessage.MsgTwrite t -> t.Tag
        | NinePMessage.MsgTclunk t -> t.Tag
        | NinePMessage.MsgTstat t -> t.Tag
        | NinePMessage.MsgTreaddir t -> t.Tag
        | NinePMessage.MsgTcreate t -> t.Tag
        | NinePMessage.MsgTwstat t -> t.Tag
        | NinePMessage.MsgTremove t -> t.Tag
        | NinePMessage.MsgTflush t -> t.Tag
        | _ -> 0us

    let getOrCreateSessionBox (sessionId: string) dialect (certificate: X509Certificate2) : SessionBox option =
        let resolvedSessionId =
            if String.IsNullOrWhiteSpace(sessionId) then defaultSessionId else sessionId

        let sessionBox =
            sessions.GetOrAdd(
                resolvedSessionId,
                Func<string, SessionBox>(fun id -> SessionBox(ProtocolSessionOps.create id dialect certificate)))

        withLock sessionBox.Gate (fun () ->
            sessionBox.State <- ProtocolSessionOps.withTransport dialect certificate sessionBox.State)

        Some sessionBox

    let tryGetGlobalFid fid =
        let mutable fs = Unchecked.defaultof<INinePFileSystem>
        if fids.TryGetValue(fid, &fs) then Some fs else None

    let tryRemoveGlobalFid fid =
        let mutable fs = Unchecked.defaultof<INinePFileSystem>
        if fids.TryRemove(fid, &fs) then Some fs else None

    let tryGetGlobalAuthFid fid =
        let mutable secure = Unchecked.defaultof<SecureString>
        if authFids.TryGetValue(fid, &secure) then Some secure else None

    let tryRemoveGlobalAuthFid fid =
        let mutable secure = Unchecked.defaultof<SecureString>
        if authFids.TryRemove(fid, &secure) then Some secure else None

    let bindFid (fid: uint32) (channel: Channel) (session: SessionBox option) : unit =
        match session with
        | None ->
            match channel.Target with
            | NamespaceNode ->
                raise (NinePProtocolException("Legacy non-session path cannot bind virtual namespace channels."))
            | BackendNode(target, relativePath) ->
                let fs = target.CreateSession()
                fs.Dialect <- NinePDialect.NineP2000
                if not (List.isEmpty relativePath) then
                    let walk = fs.WalkAsync(Twalk(0us, 0u, 0u, relativePath |> List.toArray)).GetAwaiter().GetResult()
                    if isNull walk.Wqid || walk.Wqid.Length <> relativePath.Length then
                        raise (NinePProtocolException("Unable to materialize backend path"))
                fids.[fid] <- fs
        | Some sessionBox ->
            withLock sessionBox.Gate (fun () -> sessionBox.State <- ProtocolSessionOps.bindFid fid channel sessionBox.State)

    let getFileSystemOrThrow (fid: uint32) (session: SessionBox option) : INinePFileSystem =
        match session with
        | None ->
            match tryGetGlobalFid fid with
            | Some fs -> fs
            | None -> raise (NinePProtocolException("Unknown FID"))
        | Some sessionBox ->
            withLock sessionBox.Gate (fun () ->
                match ProtocolSessionOps.tryFindFid fid sessionBox.State with
                | Some channel ->
                    match channel.Target with
                    | NamespaceNode -> raise (NinePProtocolException("Virtual namespace nodes are not backed by a persistent filesystem"))
                    | BackendNode(target, relativePath) ->
                        let fs = target.CreateSession()
                        if not (List.isEmpty relativePath) then
                            let walk = fs.WalkAsync(Twalk(0us, 0u, 0u, relativePath |> List.toArray)).GetAwaiter().GetResult()
                            if isNull walk.Wqid || walk.Wqid.Length <> relativePath.Length then
                                raise (NinePProtocolException("Unable to materialize backend path"))
                        fs
                | None -> raise (NinePProtocolException("Unknown FID")))

    let getChannelOrThrow (fid: uint32) (session: SessionBox option) : Channel =
        match session with
        | None -> raise (NinePProtocolException("Session state is required"))
        | Some sessionBox ->
            withLock sessionBox.Gate (fun () ->
                match ProtocolSessionOps.tryFindFid fid sessionBox.State with
                | Some channel -> channel
                | None -> raise (NinePProtocolException("Unknown FID")))

    let consumeAuthFid (afid: uint32) (session: SessionBox option) : SecureString option =
        match session with
        | None -> tryRemoveGlobalAuthFid afid
        | Some sessionBox ->
            withLock sessionBox.Gate (fun () ->
                match ProtocolSessionOps.tryFindAuthFid afid sessionBox.State with
                | Some secure ->
                    sessionBox.State <- ProtocolSessionOps.removeAuthFid afid sessionBox.State
                    Some secure
                | None -> None)

    let tryFindAuthFid (fid: uint32) (session: SessionBox option) : SecureString option =
        match session with
        | None -> tryGetGlobalAuthFid fid
        | Some sessionBox ->
            withLock sessionBox.Gate (fun () ->
                ProtocolSessionOps.tryFindAuthFid fid sessionBox.State)

    let gateKey (session: SessionBox option) (fid: uint32) =
        let scope =
            match session with
            | None -> "__global"
            | Some sessionBox -> sessionBox.State.SessionId
        $"{scope}:{fid}"

    let getOperationGate (session: SessionBox option) (fid: uint32) =
        fidOperationGates.GetOrAdd(gateKey session fid, fun _ -> new SemaphoreSlim(1, 1))

    let withFidLocks (session: SessionBox option) (fidsToLock: seq<uint32>) (action: unit -> Task<obj>) : Task<obj> =
        task {
            let gates =
                fidsToLock
                |> Seq.distinct
                |> Seq.sort
                |> Seq.map (getOperationGate session)
                |> Seq.toArray

            for gate in gates do
                do! gate.WaitAsync()

            try
                return! action()
            finally
                for i = gates.Length - 1 downto 0 do
                    gates.[i].Release() |> ignore
        }

    let getSessionStateOrThrow (session: SessionBox option) =
        match session with
        | Some sessionBox -> withLock sessionBox.Gate (fun () -> sessionBox.State)
        | None -> raise (NinePProtocolException("Session state is required"))

    let updateSessionState (session: SessionBox option) updater =
        match session with
        | Some sessionBox ->
            withLock sessionBox.Gate (fun () ->
                sessionBox.State <- updater sessionBox.State)
        | None -> ()

    let parseBackendReaddirNames (data: ReadOnlyMemory<byte>) =
        let names = ResizeArray<string>()
        let span = data.Span
        let mutable offset = 0

        while offset < span.Length do
            if span.Length - offset < 24 then
                offset <- span.Length
            else
                offset <- offset + 1 + 4 + 8
                offset <- offset + 8
                offset <- offset + 1
                let nameLen = int (BitConverter.ToUInt16(span.Slice(offset, 2)))
                offset <- offset + 2
                if nameLen < 0 || offset + nameLen > span.Length then
                    offset <- span.Length
                else
                    let name = Encoding.UTF8.GetString(span.Slice(offset, nameLen))
                    offset <- offset + nameLen
                    names.Add(name)

        names.ToArray()

    let getMountDirectoryNamesAsync (dialect: NinePDialect) (branches: MountBranch list) =
        task {
            let seen = System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            let names = ResizeArray<string>()

            for branch in branches do
                let! remoteFs =
                    if branch.Target.IsRemote then
                        attachResolver.TryCreateRemoteFileSystemAsync(branch.Target.MountPath)
                    else
                        Task.FromResult<INinePFileSystem>(branch.Target.CreateSession())

                let fs =
                    if isNull remoteFs then
                        raise (NinePProtocolException($"No backend found for mount '{branch.Target.MountPath}'"))
                    else
                        remoteFs

                fs.Dialect <- dialect
                let! page = fs.ReaddirAsync(Treaddir(0u, 0us, 0u, 0UL, UInt32.MaxValue))
                for name in parseBackendReaddirNames page.Data do
                    if not (String.IsNullOrWhiteSpace(name)) && seen.Add(name) then
                        names.Add(name)

            return names.ToArray()
        }

    let hasMountedChildren (ns: Namespace) (currentPath: string list) =
        ns.Mounts
        |> List.exists (fun mount ->
            hasPrefix mount.TargetPath currentPath && mount.TargetPath.Length > currentPath.Length)

    let getVirtualChildNamesAsync (dialect: NinePDialect) (ns: Namespace) (currentPath: string list) =
        task {
            let seen = System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            let names = ResizeArray<string>()

            let exactResolution = NamespaceOps.resolveWithMode LookupMode.BindTarget currentPath ns
            if exactResolution.MatchedMount.IsSome then
                let! mountNames = getMountDirectoryNamesAsync dialect exactResolution.Branches
                for name in mountNames do
                    if seen.Add(name) then
                        names.Add(name)

            for mount in ns.Mounts do
                if hasPrefix mount.TargetPath currentPath && mount.TargetPath.Length > currentPath.Length then
                    let name = mount.TargetPath.[currentPath.Length]
                    if not (String.IsNullOrEmpty(name)) && seen.Add(name) then
                        names.Add(name)

            if List.isEmpty currentPath then
                let! remoteMountPaths = attachResolver.GetRemoteMountPathsAsync()
                for mountPath in remoteMountPaths do
                    match NamespaceOps.splitPath mountPath with
                    | first :: _ when seen.Add(first) -> names.Add(first)
                    | [] -> ()
                    | _ -> ()

            return names.ToArray()
        }

    let isVirtualDirectory (ns: Namespace) (fullPath: string list) =
        let exactResolution = NamespaceOps.resolveWithMode LookupMode.BindTarget fullPath ns

        List.isEmpty fullPath
        || (exactResolution.MatchedMount.IsSome
            && (exactResolution.Branches.Length > 1 || hasMountedChildren ns fullPath))
        || hasMountedChildren ns fullPath

    let isNamespaceChannel (channel: Channel) =
        match channel.Target with
        | NamespaceNode -> true
        | BackendNode _ -> false

    let materializeBackendAsync (dialect: NinePDialect) (target: BackendTargetDescriptor) (relativePath: string list) =
        task {
            let! remoteFs =
                if target.IsRemote then
                    attachResolver.TryCreateRemoteFileSystemAsync(target.MountPath)
                else
                    Task.FromResult<INinePFileSystem>(target.CreateSession())

            let fs =
                if isNull remoteFs then
                    raise (NinePProtocolException($"No backend found for mount '{target.MountPath}'"))
                else
                    remoteFs

            fs.Dialect <- dialect

            if not (List.isEmpty relativePath) then
                let! walk = fs.WalkAsync(Twalk(0us, 0u, 0u, relativePath |> List.toArray))
                if isNull walk.Wqid || walk.Wqid.Length <> relativePath.Length then
                    raise (NinePProtocolException("Resolved backend path no longer exists"))

            return fs
        }

    let tryResolveBranchPathAsync (dialect: NinePDialect) (branches: MountBranch list) (remainder: string list) =
        task {
            let mutable resolved : (BackendTargetDescriptor * string list) option = None

            for branch in branches do
                if resolved.IsNone then
                    try
                        let! fs = materializeBackendAsync dialect branch.Target []
                        if List.isEmpty remainder then
                            resolved <- Some(branch.Target, [])
                        else
                            let! walk = fs.WalkAsync(Twalk(0us, 0u, 0u, remainder |> List.toArray))
                            if not (isNull walk.Wqid) && walk.Wqid.Length = remainder.Length then
                                resolved <- Some(branch.Target, remainder)
                    with
                    | :? NinePProtocolException -> ()

            return resolved
        }

    let dispatchWithChannelAsync
        (dialect: NinePDialect)
        (channel: Channel)
        (action: INinePFileSystem -> Task<obj>)
        : Task<obj> =
        task {
            match channel.Target with
            | NamespaceNode ->
                return raise (NinePProtocolException("Virtual namespace node"))
            | BackendNode(target, relativePath) ->
                let! fs = materializeBackendAsync dialect target relativePath
                return! action fs
        }

    let dispatchCreateIntoNamespaceAsync
        (tag: uint16)
        (fid: uint32)
        (dialect: NinePDialect)
        (channel: Channel)
        (t: Tcreate)
        (session: SessionBox option)
        : Task<obj> =
        task {
            let stateSnapshot = getSessionStateOrThrow session
            let currentPath = channel.InternalPath

            match NamespaceOps.trySelectCreateTarget currentPath (ProtocolSessionOps.namespaceOf stateSnapshot) with
            | None ->
                let mountedPath = "/" + String.Join("/", currentPath)
                return raise (NinePProtocolException($"No creatable backend mounted at '{mountedPath}'"))
            | Some target ->
                let! fs = materializeBackendAsync dialect target []
                let! response = fs.CreateAsync(t)

                let createdPath = currentPath @ [ t.Name ]
                let rebound =
                    createBackendBinding target [ t.Name ] createdPath response.Qid.Type response.Qid.Version response.Qid.Path

                match session with
                | Some sessionBox ->
                    withLock sessionBox.Gate (fun () ->
                        sessionBox.State <- ProtocolSessionOps.bindFid fid rebound sessionBox.State)
                | None -> ()

                return response :> obj
        }

    let dispatchCreateIntoBackendAsync
        (fid: uint32)
        (dialect: NinePDialect)
        (channel: Channel)
        (t: Tcreate)
        (session: SessionBox option)
        : Task<obj> =
        task {
            match channel.Target with
            | NamespaceNode ->
                return raise (NinePProtocolException("Virtual namespace node"))
            | BackendNode(target, relativePath) ->
                let! fs = materializeBackendAsync dialect target relativePath
                let! response = fs.CreateAsync(t)

                let rebound =
                    createBackendBinding
                        target
                        (relativePath @ [ t.Name ])
                        (channel.InternalPath @ [ t.Name ])
                        response.Qid.Type
                        response.Qid.Version
                        response.Qid.Path

                match session with
                | Some sessionBox ->
                    withLock sessionBox.Gate (fun () ->
                        sessionBox.State <- ProtocolSessionOps.bindFid fid rebound sessionBox.State)
                | None -> ()

                return response :> obj
        }

    let tryResolveVirtualPathAsync (state: ProtocolSession) (dialect: NinePDialect) (pathState: PathState) =
        task {
            let fullPath = pathState.VisiblePath
            let resolution = NamespaceOps.resolveWithMode LookupMode.Walk fullPath (ProtocolSessionOps.namespaceOf state)
            let directAttachRoot =
                match resolution.MatchedMount with
                | Some mount ->
                    List.isEmpty fullPath
                    && List.isEmpty mount.TargetPath
                    && (ProtocolSessionOps.namespaceOf state).Mounts.Length = 1
                    && resolution.Branches.Length = 1
                | None -> false
            let namespaceOwnedExactPath =
                resolution.MatchedMount.IsSome
                && List.isEmpty resolution.Remainder
                && not directAttachRoot
                && isVirtualDirectory (ProtocolSessionOps.namespaceOf state) fullPath

            match resolution.Targets with
            | _ when namespaceOwnedExactPath ->
                let qid = syntheticDirectoryQid fullPath
                return Some(createVirtualBindingWithPathState dialect pathState qid.Type qid.Version qid.Path, qid)
            | _ when not (List.isEmpty resolution.Remainder) ->
                let! selected = tryResolveBranchPathAsync dialect resolution.Branches resolution.Remainder
                match selected with
                | Some(target, relativePath) ->
                    let qid =
                        if List.isEmpty relativePath then syntheticDirectoryQid fullPath else syntheticFileQid fullPath

                    let resolvedPathState =
                        match resolution.MatchedMount with
                        | Some mount when List.isEmpty relativePath ->
                            match pathState.MountHistory with
                            | head :: _ when head.MountId = mount.Chain.MountId -> pathState
                            | _ ->
                                { pathState with
                                    MountHistory =
                                        { MountId = mount.Chain.MountId
                                          MountPath = mount.TargetPath
                                          ExitPath = parentPath mount.TargetPath }
                                        :: pathState.MountHistory }
                        | _ -> pathState

                    return Some(createBackendBindingWithPathState target relativePath resolvedPathState qid.Type qid.Version qid.Path, qid)
                | None ->
                    if isVirtualDirectory (ProtocolSessionOps.namespaceOf state) fullPath then
                        let qid = syntheticDirectoryQid fullPath
                        return Some(createVirtualBindingWithPathState dialect pathState qid.Type qid.Version qid.Path, qid)
                    else
                        return None
            | target :: _ ->
                let qid =
                    if List.isEmpty resolution.Remainder then syntheticDirectoryQid fullPath else syntheticFileQid fullPath

                let resolvedPathState =
                    match resolution.MatchedMount with
                    | Some mount when List.isEmpty resolution.Remainder ->
                        match pathState.MountHistory with
                        | head :: _ when head.MountId = mount.Chain.MountId -> pathState
                        | _ ->
                            { pathState with
                                MountHistory =
                                    { MountId = mount.Chain.MountId
                                      MountPath = mount.TargetPath
                                      ExitPath = parentPath mount.TargetPath }
                                    :: pathState.MountHistory }
                    | _ -> pathState

                return Some(createBackendBindingWithPathState target resolution.Remainder resolvedPathState qid.Type qid.Version qid.Path, qid)
            | [] ->
                if isVirtualDirectory (ProtocolSessionOps.namespaceOf state) fullPath then
                    let qid = syntheticDirectoryQid fullPath
                    return Some(createVirtualBindingWithPathState dialect pathState qid.Type qid.Version qid.Path, qid)
                elif List.isEmpty fullPath then
                    return None
                else
                    let remoteMountPath = "/" + fullPath.Head
                    let! remoteProbe = attachResolver.TryCreateRemoteFileSystemAsync(remoteMountPath)
                    if isNull remoteProbe then
                        return None
                    else
                        let relative = if fullPath.Length > 1 then fullPath |> List.skip 1 else []
                        let descriptor = BackendTargetDescriptor.Remote(remoteMountPath)
                        let qid =
                            if List.isEmpty relative then syntheticDirectoryQid fullPath else syntheticFileQid fullPath
                        let remotePathState =
                            if List.isEmpty relative then
                                match pathState.MountHistory with
                                | head :: _ when head.MountPath = [ fullPath.Head ] -> pathState
                                | _ ->
                                    { pathState with
                                        MountHistory =
                                            { MountId = stableSyntheticQidPath 'm' [ fullPath.Head ]
                                              MountPath = [ fullPath.Head ]
                                              ExitPath = [] }
                                            :: pathState.MountHistory }
                            else
                                pathState

                        return Some(createBackendBindingWithPathState descriptor relative remotePathState qid.Type qid.Version qid.Path, qid)
        }

    let encodeVirtualReadEntries (_dialect: NinePDialect) (currentPath: string list) (childNames: string array) =
        let entries = ResizeArray<byte>()
        for name in childNames do
            let stat =
                Stat(
                    0us,
                    0us,
                    0u,
                    syntheticDirectoryQid (currentPath @ [ name ]),
                    0755u ||| uint32 NinePConstants.FileMode9P.DMDIR,
                    0u,
                    0u,
                    0UL,
                    name,
                    "scott",
                    "scott",
                    "scott",
                    dialect = NinePDialect.NineP2000)
            let buffer = Array.zeroCreate<byte> (int stat.Size)
            let mutable offset = 0
            stat.WriteTo(buffer, &offset)
            entries.AddRange(buffer)
        entries.ToArray()

    let sliceVirtualReadData tag (offset: uint64) (count: uint32) (allData: byte array) =
        if offset >= uint64 allData.Length then
            Rread(tag, Array.empty<byte>) :> obj
        else
            let mutable totalToSend = 0
            let mutable currentOffset = int offset
            while currentOffset + 2 <= allData.Length do
                let entrySize = int (BitConverter.ToUInt16(allData, currentOffset)) + 2
                if entrySize <= 0 || currentOffset + entrySize > allData.Length || totalToSend + entrySize > int count then
                    currentOffset <- allData.Length
                else
                    totalToSend <- totalToSend + entrySize
                    currentOffset <- currentOffset + entrySize
            if totalToSend = 0 then
                Rread(tag, Array.empty<byte>) :> obj
            else
                Rread(tag, allData.AsMemory(int offset, totalToSend).ToArray()) :> obj

    let handleVirtualRead (tag: uint16) (dialect: NinePDialect) (currentPath: string list) (offset: uint64) (count: uint32) (ns: Namespace) =
        task {
            let! childNames = getVirtualChildNamesAsync dialect ns currentPath
            let allData = encodeVirtualReadEntries dialect currentPath childNames
            return sliceVirtualReadData tag offset count allData
        }

    let handleVirtualStat (tag: uint16) (dialect: NinePDialect) (currentPath: string list) =
        let name =
            match List.rev currentPath with
            | [] -> "/"
            | head :: _ -> head

        let stat =
            Stat(
                0us,
                0us,
                0u,
                syntheticDirectoryQid currentPath,
                0755u ||| uint32 NinePConstants.FileMode9P.DMDIR,
                0u,
                0u,
                0UL,
                name,
                "scott",
                "scott",
                "scott",
                dialect = dialect)

        Rstat(tag, stat) :> obj

    let handleVirtualOpen (tag: uint16) (currentPath: string list) =
        Ropen(tag, syntheticDirectoryQid currentPath, 0u) :> obj

    let handleVirtualReaddir (tag: uint16) (dialect: NinePDialect) (currentPath: string list) (offset: uint64) (count: uint32) (ns: Namespace) =
        task {
            let! childNames = getVirtualChildNamesAsync dialect ns currentPath
            let encodedEntries = ResizeArray<struct (uint64 * byte array)>()
            let mutable nextOffset = 0UL

            for name in childNames do
                nextOffset <- nextOffset + 1UL
                let qid = syntheticDirectoryQid (currentPath @ [ name ])
                let nameLen = Encoding.UTF8.GetByteCount(name)
                let entrySize = 13 + 8 + 1 + 2 + nameLen
                let entry = Array.zeroCreate<byte> entrySize
                let mutable writeOffset = 0
                entry.[writeOffset] <- byte qid.Type
                writeOffset <- writeOffset + 1
                BitConverter.GetBytes(qid.Version).CopyTo(entry, writeOffset)
                writeOffset <- writeOffset + 4
                BitConverter.GetBytes(qid.Path).CopyTo(entry, writeOffset)
                writeOffset <- writeOffset + 8
                BitConverter.GetBytes(nextOffset).CopyTo(entry, writeOffset)
                writeOffset <- writeOffset + 8
                entry.[writeOffset] <- byte qid.Type
                writeOffset <- writeOffset + 1
                BitConverter.GetBytes(uint16 nameLen).CopyTo(entry, writeOffset)
                writeOffset <- writeOffset + 2
                Encoding.UTF8.GetBytes(name).CopyTo(entry, writeOffset)
                encodedEntries.Add(struct (nextOffset, entry))

            let startIndex =
                if offset = 0UL then
                    0
                else
                    encodedEntries
                    |> Seq.tryFindIndex (fun struct (next, _) -> next > offset)
                    |> Option.defaultValue -1

            if startIndex < 0 then
                return Rreaddir(uint32 (NinePConstants.HeaderSize + 4), tag, 0u, Array.empty<byte>) :> obj
            else
                let page = ResizeArray<byte>()
                for i in startIndex .. encodedEntries.Count - 1 do
                    let struct (_, entry) = encodedEntries.[i]
                    if page.Count + entry.Length <= int count then
                        page.AddRange(entry)
                let chunk = page.ToArray()
                return Rreaddir(uint32 chunk.Length + uint32 (NinePConstants.HeaderSize + 4), tag, uint32 chunk.Length, chunk) :> obj
        }

    let handleAttach (t: Tattach) dialect (certificate: X509Certificate2) session : Task<obj> =
        task {
            let credentials =
                if t.Afid = NinePConstants.NoFid then
                    null
                else
                    match consumeAuthFid t.Afid session with
                    | Some secure when secure.Length > 0 ->
                        if not (secure.IsReadOnly()) then
                            secure.MakeReadOnly()
                        secure
                    | Some secure ->
                        secure.Dispose()
                        null
                    | None -> null

            if String.IsNullOrEmpty(t.Aname) || t.Aname = "/" then
                let ns = buildRootNamespace certificate
                updateSessionState session (ProtocolSessionOps.withNamespace ns)
                let qid = syntheticDirectoryQid []
                bindFid t.Fid (createVirtualBinding dialect [] qid.Type qid.Version qid.Path) session
            else
                let! resolution = attachResolver.ResolveAsync(t.Aname, credentials, certificate)
                let target =
                    match resolution.Target with
                    | null -> raise (NinePProtocolException("Attach resolution did not produce a backend target"))
                    | value -> value

                let directNamespace =
                    { Mounts =
                        [ { TargetPath = []
                            Chain =
                                { MountId = 1UL
                                  Branches =
                                      [ { Target = target
                                          Flags = BindFlags.MREPL } ] } } ] }
                updateSessionState session (ProtocolSessionOps.withNamespace directNamespace)
                let qid = syntheticDirectoryQid []
                bindFid t.Fid (createBackendBinding target [] [] qid.Type qid.Version qid.Path) session
            return Rattach(t.Tag, Qid(QidType.QTDIR, 0u, 0UL)) :> obj
        }

    let handleWalk (t: Twalk) (dialect: NinePDialect) (session: SessionBox option) : Task<obj> =
        task {
            match session with
            | None ->
                let fs = getFileSystemOrThrow t.Fid None
                if t.NewFid <> t.Fid && fids.ContainsKey(t.NewFid) then
                    return Rerror(t.Tag, $"newfid {t.NewFid} already exists") :> obj
                else
                    let targetFs = fs.Clone()
                    targetFs.Dialect <- fs.Dialect
                    let! response = targetFs.WalkAsync(t)
                    let mutable addConflict : obj option = None

                    if t.Wname.Length = 0 || (not (isNull response.Wqid) && response.Wqid.Length = t.Wname.Length) then
                        if t.NewFid = t.Fid then
                            fids.[t.NewFid] <- targetFs
                        elif not (fids.TryAdd(t.NewFid, targetFs)) then
                            addConflict <- Some (Rerror(t.Tag, $"newfid {t.NewFid} was claimed by another thread") :> obj)

                    match addConflict with
                    | Some errorResponse -> return errorResponse
                    | None -> return response :> obj

            | Some sessionBox ->
                let sourceChannel : Channel =
                    withLock sessionBox.Gate (fun () ->
                        match ProtocolSessionOps.tryFindFid t.Fid sessionBox.State with
                        | Some channel when t.NewFid <> t.Fid && ProtocolSessionOps.containsFid t.NewFid sessionBox.State ->
                            raise (NinePProtocolException(sprintf "newfid %u already exists" t.NewFid))
                        | Some channel -> channel
                        | None -> raise (NinePProtocolException("Unknown FID")))

                let stateSnapshot = getSessionStateOrThrow session
                let qids = ResizeArray<NinePSharp.Constants.Qid>()
                let mutable lastChannel = sourceChannel
                let mutable failed = false

                if t.Wname.Length = 0 then
                    lastChannel <- sourceChannel
                else
                    for segment in t.Wname do
                        if not failed then
                            let tentative = ChannelOps.walk [ segment ] lastChannel
                            let! resolved = tryResolveVirtualPathAsync stateSnapshot dialect tentative.PathState
                            match resolved with
                            | Some (channel, qid) ->
                                qids.Add(qid)
                                lastChannel <- channel
                            | None ->
                                failed <- true

                let mutable updateFailure : obj option = None

                if t.Wname.Length = 0 || qids.Count = t.Wname.Length then
                    updateFailure <-
                        withLock sessionBox.Gate (fun () ->
                            match ProtocolSessionOps.tryFindFid t.Fid sessionBox.State with
                            | None ->
                                Some (Rerror(t.Tag, $"fid {t.Fid} was removed during walk") :> obj)
                            | Some _ when t.NewFid <> t.Fid && ProtocolSessionOps.containsFid t.NewFid sessionBox.State ->
                                Some (Rerror(t.Tag, $"newfid {t.NewFid} was claimed by another thread") :> obj)
                            | Some _ ->
                                sessionBox.State <- ProtocolSessionOps.bindFid t.NewFid lastChannel sessionBox.State
                                None)

                match updateFailure with
                | Some errorResponse -> return errorResponse
                | None -> return Rwalk(t.Tag, if qids.Count = 0 then null else qids.ToArray()) :> obj
        }

    let handleWrite (t: Twrite) dialect session : Task<obj> =
        task {
            match tryFindAuthFid t.Fid session with
            | Some secure ->
                let byteBuffer = t.Data.ToArray()
                let decoder = Encoding.UTF8.GetDecoder()
                let maxChars = Encoding.UTF8.GetMaxCharCount(byteBuffer.Length)
                let charBuffer = ArrayPool<char>.Shared.Rent(maxChars)

                try
                    let charsDecoded = decoder.GetChars(byteBuffer, 0, byteBuffer.Length, charBuffer, 0, true)
                    for i in 0 .. charsDecoded - 1 do
                        secure.AppendChar(charBuffer.[i])
                finally
                    Array.Clear(byteBuffer, 0, byteBuffer.Length)
                    Array.Clear(charBuffer, 0, charBuffer.Length)
                    ArrayPool<char>.Shared.Return(charBuffer)

                return Rwrite(t.Tag, uint32 t.Data.Length) :> obj

            | None ->
                let channel = getChannelOrThrow t.Fid session
                return!
                    dispatchWithChannelAsync dialect channel (fun fs ->
                        task {
                            let! response = fs.WriteAsync(t)
                            return response :> obj
                        })
        }

    let handleClunk (t: Tclunk) (session: SessionBox option) : Task<obj> =
        task {
            let mutable authRemoved = false
            let mutable removedChannel = Unchecked.defaultof<Channel>
            let mutable hadChannel = false

            match session with
            | None ->
                match tryRemoveGlobalAuthFid t.Fid with
                | Some secure ->
                    secure.Dispose()
                    authRemoved <- true
                | None -> ()

                match tryRemoveGlobalFid t.Fid with
                | Some _ -> hadChannel <- false
                | None -> ()

            | Some sessionBox ->
                withLock sessionBox.Gate (fun () ->
                    match ProtocolSessionOps.tryFindAuthFid t.Fid sessionBox.State with
                    | Some secure ->
                        secure.Dispose()
                        authRemoved <- true
                        sessionBox.State <- ProtocolSessionOps.removeAuthFid t.Fid sessionBox.State
                    | None -> ()

                    match ProtocolSessionOps.tryFindFid t.Fid sessionBox.State with
                    | Some channel ->
                        removedChannel <- channel
                        hadChannel <- true
                        sessionBox.State <- ProtocolSessionOps.removeFid t.Fid sessionBox.State
                    | None -> ())

            if hadChannel then
                match removedChannel.Target with
                | NamespaceNode -> return Rclunk(t.Tag) :> obj
                | BackendNode _ -> return Rclunk(t.Tag) :> obj
            elif authRemoved then
                return Rclunk(t.Tag) :> obj
            else
                return Rerror(t.Tag, $"Unknown FID: {t.Fid}") :> obj
        }

    let handleRemove (t: Tremove) (dialect: NinePDialect) (session: SessionBox option) : Task<obj> =
        task {
            let mutable removedChannel = Unchecked.defaultof<Channel>
            let mutable hadChannel = false

            match session with
            | None ->
                match tryRemoveGlobalFid t.Fid with
                | Some _ -> hadChannel <- false
                | None -> ()

            | Some sessionBox ->
                withLock sessionBox.Gate (fun () ->
                    match ProtocolSessionOps.tryFindFid t.Fid sessionBox.State with
                    | Some channel ->
                        removedChannel <- channel
                        hadChannel <- true
                        sessionBox.State <- ProtocolSessionOps.removeFid t.Fid sessionBox.State
                    | None -> ())

            if not hadChannel then
                raise (NinePProtocolException("Unknown FID"))

            match removedChannel.Target with
            | NamespaceNode ->
                return raise (NinePNotSupportedException())
            | BackendNode _ ->
                return!
                    dispatchWithChannelAsync dialect removedChannel (fun fs ->
                        task {
                            let! response = fs.RemoveAsync(t)
                            return response :> obj
                        })
        }

    interface INinePFSDispatcher with
        member this.DispatchAsync(message, dialect, certificate) =
            (this :> INinePFSDispatcher).DispatchAsync(defaultSessionId, message, dialect, certificate)

        member _.DispatchAsync(sessionId, message, dialect, certificate) : Task<obj> =
            task {
                let tag = getTag message
                let session = getOrCreateSessionBox sessionId dialect certificate

                try
                    match message with
                    | NinePMessage.MsgTversion t ->
                        let versionStr =
                            match t.Version with
                            | "9P2000.L" -> "9P2000.L"
                            | "9P2000.u" -> "9P2000.u"
                            | _ -> "9P2000"

                        return Rversion(t.Tag, t.MSize, versionStr) :> obj

                    | NinePMessage.MsgTauth t ->
                        return! withFidLocks session [ t.Afid ] (fun () ->
                            task {
                                let secure = new SecureString()
                                match session with
                                | None -> authFids.[t.Afid] <- secure
                                | Some sessionBox ->
                                    withLock sessionBox.Gate (fun () ->
                                        sessionBox.State <- ProtocolSessionOps.addAuthFid t.Afid secure sessionBox.State)
                                return Rauth(t.Tag, Qid(QidType.QTAUTH, 0u, uint64 t.Afid)) :> obj
                            })

                    | NinePMessage.MsgTflush t ->
                        return Rflush(t.Tag) :> obj

                    | NinePMessage.MsgTattach t ->
                        let fidsToLock =
                            if t.Afid = NinePConstants.NoFid then [ t.Fid ] else [ t.Afid; t.Fid ]
                        return! withFidLocks session fidsToLock (fun () -> handleAttach t dialect certificate session)

                    | NinePMessage.MsgTwalk t ->
                        return! withFidLocks session [ t.Fid; t.NewFid ] (fun () -> handleWalk t dialect session)

                    | NinePMessage.MsgTclunk t ->
                        return! withFidLocks session [ t.Fid ] (fun () -> handleClunk t session)

                    | NinePMessage.MsgTremove t ->
                        return! withFidLocks session [ t.Fid ] (fun () -> handleRemove t dialect session)

                    | NinePMessage.MsgTstat t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                if isNamespaceChannel channel then
                                    return handleVirtualStat t.Tag dialect channel.InternalPath
                                else
                                    return! dispatchWithChannelAsync dialect channel (fun fs ->
                                        task {
                                            let! response = fs.StatAsync(t)
                                            return response :> obj
                                        })
                            })

                    | NinePMessage.MsgTreaddir t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                if isNamespaceChannel channel then
                                    let stateSnapshot = getSessionStateOrThrow session
                                    return! handleVirtualReaddir t.Tag dialect channel.InternalPath t.Offset t.Count (ProtocolSessionOps.namespaceOf stateSnapshot)
                                else
                                    return! dispatchWithChannelAsync dialect channel (fun fs ->
                                        task {
                                            let! response = fs.ReaddirAsync(t)
                                            return response :> obj
                                        })
                            })

                    | NinePMessage.MsgTcreate t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                if isNamespaceChannel channel then
                                    return! dispatchCreateIntoNamespaceAsync t.Tag t.Fid dialect channel t session
                                else
                                    return! dispatchCreateIntoBackendAsync t.Fid dialect channel t session
                            })

                    | NinePMessage.MsgTwstat t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            let channel = getChannelOrThrow t.Fid session
                            dispatchWithChannelAsync dialect channel (fun fs ->
                                task {
                                    let! response = fs.WstatAsync(t)
                                    return response :> obj
                                }))

                    | NinePMessage.MsgTopen t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                if isNamespaceChannel channel then
                                    return handleVirtualOpen t.Tag channel.InternalPath
                                else
                                    return! dispatchWithChannelAsync dialect channel (fun fs ->
                                        task {
                                            let! response = fs.OpenAsync(t)
                                            return response :> obj
                                        })
                            })

                    | NinePMessage.MsgTread t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                if isNamespaceChannel channel then
                                    let stateSnapshot = getSessionStateOrThrow session
                                    return! handleVirtualRead t.Tag dialect channel.InternalPath t.Offset t.Count (ProtocolSessionOps.namespaceOf stateSnapshot)
                                else
                                    return! dispatchWithChannelAsync dialect channel (fun fs ->
                                        task {
                                            let! response = fs.ReadAsync(t)
                                            return response :> obj
                                        })
                            })

                    | NinePMessage.MsgTwrite t ->
                        return! withFidLocks session [ t.Fid ] (fun () -> handleWrite t dialect session)

                    | _ ->
                        return raise (NinePProtocolException("Message type not implemented or supported"))
                with
                | :? NinePProtocolException as ex ->
                    return createErrorResponse tag dialect ex
                | _ ->
                    return createErrorResponse tag dialect (NinePProtocolException("Internal Server Error"))
            }
