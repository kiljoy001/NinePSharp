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
open NinePSharp.Server.Abstractions.Utils
open NinePSharp.Server.Interfaces
open NinePSharp.Server.Utils

/// Tracks an in-flight request for flush support (9front semantics).
type private InFlightRequest =
    { Tag: uint16
      Cts: CancellationTokenSource
      Completion: TaskCompletionSource<unit> }

type private SessionBox(state: ProtocolSession) =
    let gate = obj()
    let inFlightRequests = ConcurrentDictionary<uint16, InFlightRequest>()
    member _.Gate = gate
    member val State = state with get, set
    member _.InFlightRequests = inFlightRequests

type private VirtualDirEntry =
    { QidType: QidType
      Name: string }

type NinePFSDispatcherEngine(attachResolver: IAttachResolver) =
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
        ChannelOps.createBackendNode { Type = qidType; Version = qidVersion; Path = qidPath } target relativePath visiblePath

    let createBackendBindingWithPathState (target: BackendTargetDescriptor) (relativePath: string list) (pathState: PathState) (qidType: QidType) (qidVersion: uint32) (qidPath: uint64) =
        ChannelOps.createBackendNodeWithPathState { Type = qidType; Version = qidVersion; Path = qidPath } target relativePath pathState

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
        let chains =
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
                let mountId = uint64 ((ordered |> List.head |> fun struct (index, _) -> index) + 1)
                let key = NamespaceOps.mountKeyForPath targetPath
                key,
                    { MountId = mountId
                      From = key
                      MountPath = targetPath
                      Branches =
                          ordered
                          |> List.mapi (fun branchIndex struct (_, mount) ->
                              { Target = mount.Target
                                Flags = if branchIndex = 0 then BindFlags.MCREATE else BindFlags.MAFTER }) })
            |> Seq.toList

        { MountHash = chains |> Map.ofList }

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

    let getOrCreateSessionBox (sessionId: string) dialect (certificate: X509Certificate2) : SessionBox =
        if String.IsNullOrWhiteSpace(sessionId) then
            raise (ArgumentException("sessionId is required", nameof(sessionId)))
        let sessionBox =
            sessions.GetOrAdd(
                sessionId,
                Func<string, SessionBox>(fun id -> SessionBox(ProtocolSessionOps.create id dialect certificate)))

        withLock sessionBox.Gate (fun () ->
            sessionBox.State <- ProtocolSessionOps.withTransport dialect certificate sessionBox.State)

        sessionBox

    let bindFid (fid: uint32) (channel: Channel) (session: SessionBox) : unit =
        withLock session.Gate (fun () -> session.State <- ProtocolSessionOps.bindFid fid channel session.State)

    /// Track an in-flight request for flush support (9front semantics).
    /// Returns CancellationToken that handlers should check.
    let withInFlightTracking (tag: uint16) (session: SessionBox) (action: CancellationToken -> Task<obj>) : Task<obj> =
        task {
            let cts = new CancellationTokenSource()
            let completion = TaskCompletionSource<unit>()
            let inFlight = { Tag = tag; Cts = cts; Completion = completion }

            // Register this request as in-flight
            session.InFlightRequests.[tag] <- inFlight

            try
                let! result = action cts.Token
                return result
            finally
                // Mark as complete and unregister
                completion.TrySetResult(()) |> ignore
                session.InFlightRequests.TryRemove(tag) |> ignore
                cts.Dispose()
        }

    let updateChannel (fid: uint32) (updater: Channel -> Channel) (session: SessionBox) : unit =
        withLock session.Gate (fun () ->
            match ProtocolSessionOps.tryFindFid fid session.State with
            | Some channel -> session.State <- ProtocolSessionOps.bindFid fid (updater channel) session.State
            | None -> ())

    let updateChannelOffset (fid: uint32) (newOffset: uint64) (session: SessionBox) : unit =
        updateChannel fid (fun channel -> { channel with Offset = newOffset }) session

    let markChannelOpened (fid: uint32) (qid: NinePSharp.Constants.Qid option) (session: SessionBox) : unit =
        updateChannel
            fid
            (fun channel ->
                let nextQid =
                    match qid with
                    | Some value -> { Type = value.Type; Version = value.Version; Path = value.Path }
                    | None -> channel.Qid

                { channel with
                    Qid = nextQid
                    Offset = 0UL
                    IsOpened = true
                    Umc = None
                    Uri = 0 })
            session

    let getSessionUserName (session: SessionBox) =
        withLock session.Gate (fun () -> session.State.UserName)

    let requireOpened (operationName: string) (channel: Channel) =
        if not channel.IsOpened then
            raise (NinePProtocolException($"FID must be opened before {operationName}"))

    let getChannelOrThrow (fid: uint32) (session: SessionBox) : Channel =
        withLock session.Gate (fun () ->
            match ProtocolSessionOps.tryFindFid fid session.State with
            | Some channel -> channel
            | None -> raise (NinePProtocolException("Unknown FID")))

    let consumeAuthFid (afid: uint32) (session: SessionBox) : SecureString option =
        withLock session.Gate (fun () ->
            match ProtocolSessionOps.tryFindAuthFid afid session.State with
            | Some secure ->
                session.State <- ProtocolSessionOps.removeAuthFid afid session.State
                Some secure
            | None -> None)

    let tryFindAuthFid (fid: uint32) (session: SessionBox) : SecureString option =
        withLock session.Gate (fun () ->
            ProtocolSessionOps.tryFindAuthFid fid session.State)

    let gateKey (session: SessionBox) (fid: uint32) =
        $"{session.State.SessionId}:{fid}"

    let getOperationGate (session: SessionBox) (fid: uint32) =
        fidOperationGates.GetOrAdd(gateKey session fid, fun _ -> new SemaphoreSlim(1, 1))

    let withFidLocks (session: SessionBox) (fidsToLock: seq<uint32>) (action: unit -> Task<obj>) : Task<obj> =
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

    let getSessionStateOrThrow (session: SessionBox) =
        withLock session.Gate (fun () -> session.State)

    let updateSessionState (session: SessionBox) updater =
        withLock session.Gate (fun () ->
            session.State <- updater session.State)

    let normalizeMountPathString (path: string) =
        "/" + String.Join("/", NamespaceOps.splitPath path)

    let parseBackendReadEntries (data: ReadOnlyMemory<byte>) =
        let entries = ResizeArray<VirtualDirEntry>()
        let bytes = data.ToArray()
        let mutable offset = 0
        while offset < bytes.Length do
            try
                let stat = Stat(bytes, &offset)
                if not (String.IsNullOrWhiteSpace(stat.Name)) then
                    entries.Add({ QidType = stat.Qid.Type; Name = stat.Name })
            with _ ->
                offset <- bytes.Length
        entries.ToArray()

    let materializeBackendRuntimeAsync (target: BackendTargetDescriptor) =
        task {
            let! remoteRuntime =
                if target.IsRemote then
                    attachResolver.TryCreateRemoteRuntimeAsync(target.MountPath)
                else
                    Task.FromResult<IBackendRuntime>(null)

            if target.IsRemote then
                if isNull remoteRuntime then
                    return raise (NinePProtocolException($"No backend found for mount '{target.MountPath}'"))
                else
                    return remoteRuntime
            else
                return target.CreateRuntime()
        }

    let remoteMountExistsAsync (mountPath: string) =
        task {
            let normalized = normalizeMountPathString mountPath
            let! remoteMountPaths = attachResolver.GetRemoteMountPathsAsync()
            return
                remoteMountPaths
                |> Seq.exists (fun candidate -> normalizeMountPathString candidate = normalized)
        }

    let getMountDirectoryEntriesAsync (dialect: NinePDialect) (branches: MountBranch list) (ct: CancellationToken) =
        task {
            ct.ThrowIfCancellationRequested()
            let seen = System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            let entries = ResizeArray<VirtualDirEntry>()

            for branch in branches do
                ct.ThrowIfCancellationRequested()
                let! runtime = materializeBackendRuntimeAsync branch.Target
                let! page = runtime.ReadAsync([||], Tread(0us, 0u, 0UL, UInt32.MaxValue), dialect, ct)
                for entry in parseBackendReadEntries page.Data do
                    if seen.Add(entry.Name) then
                        entries.Add(entry)

            return entries.ToArray()
        }

    let allMountChains (ns: Namespace) =
        ns.MountHash |> Map.toList |> List.map snd

    let hasMountedChildren (ns: Namespace) (currentPath: string list) =
        allMountChains ns
        |> List.exists (fun chain ->
            hasPrefix chain.MountPath currentPath && chain.MountPath.Length > currentPath.Length)

    let getVirtualChildEntriesAsync (dialect: NinePDialect) (ns: Namespace) (currentPath: string list) (ct: CancellationToken) =
        task {
            ct.ThrowIfCancellationRequested()
            let seen = System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            let entries = ResizeArray<VirtualDirEntry>()

            let key = NamespaceOps.mountKeyForPath currentPath
            match NamespaceOps.findMount key ns with
            | Some chain ->
                let! mountEntries = getMountDirectoryEntriesAsync dialect chain.Branches ct
                for entry in mountEntries do
                    if seen.Add(entry.Name) then
                        entries.Add(entry)
            | None -> ()

            for chain in allMountChains ns do
                if hasPrefix chain.MountPath currentPath && chain.MountPath.Length > currentPath.Length then
                    let name = chain.MountPath.[currentPath.Length]
                    if not (String.IsNullOrEmpty(name)) && seen.Add(name) then
                        entries.Add({ QidType = QidType.QTDIR; Name = name })

            if List.isEmpty currentPath then
                let! remoteMountPaths = attachResolver.GetRemoteMountPathsAsync()
                for mountPath in remoteMountPaths do
                    match NamespaceOps.splitPath mountPath with
                    | first :: _ when seen.Add(first) -> entries.Add({ QidType = QidType.QTDIR; Name = first })
                    | [] -> ()
                    | _ -> ()

            return entries.ToArray()
        }

    let isVirtualDirectory (ns: Namespace) (fullPath: string list) =
        let key = NamespaceOps.mountKeyForPath fullPath
        let mountAtPath = NamespaceOps.findMount key ns

        List.isEmpty fullPath
        || (mountAtPath.IsSome
            && (mountAtPath.Value.Branches.Length > 1 || hasMountedChildren ns fullPath))
        || hasMountedChildren ns fullPath

    let isNamespaceChannel (channel: Channel) =
        match channel.Target with
        | NamespaceNode -> true
        | BackendNode _ -> false

    let tryResolveBranchPathAsync
        (dialect: NinePDialect) (branches: MountBranch list) (remainder: string list) =
        task {
            let mutable resolved : (BackendTargetDescriptor * string list) option = None

            for branch in branches do
                if resolved.IsNone then
                    try
                        let! runtime = materializeBackendRuntimeAsync branch.Target
                        if List.isEmpty remainder then
                            resolved <- Some(branch.Target, [])
                        else
                            let! walk = runtime.WalkAsync(remainder |> List.toArray, dialect)
                            if not (isNull walk.Wqid) && walk.Wqid.Length = remainder.Length then
                                resolved <- Some(branch.Target, remainder)
                    with
                    | :? NinePProtocolException -> ()

            return resolved
        }

    let dispatchWithChannelAsync
        (dialect: NinePDialect)
        (channel: Channel)
        (action: IBackendRuntime * string array -> Task<obj>)
        : Task<obj> =
        task {
            match channel.Target with
            | NamespaceNode ->
                return raise (NinePProtocolException("Virtual namespace node"))
            | BackendNode(target, relativePath) ->
                let! runtime = materializeBackendRuntimeAsync target
                return! action (runtime, relativePath |> List.toArray)
        }

    let dispatchCreateIntoNamespaceAsync
        (tag: uint16)
        (fid: uint32)
        (dialect: NinePDialect)
        (channel: Channel)
        (t: Tcreate)
        (session: SessionBox)
        : Task<obj> =
        task {
            let stateSnapshot = getSessionStateOrThrow session
            let currentPath = channel.InternalPath

            match NamespaceOps.trySelectCreateTarget (currentPath @ [ t.Name ]) (ProtocolSessionOps.namespaceOf stateSnapshot) with
            | None ->
                let mountedPath = "/" + String.Join("/", currentPath)
                return raise (NinePProtocolException($"No creatable backend mounted at '{mountedPath}'"))
            | Some target ->
                let! runtime = materializeBackendRuntimeAsync target
                let! response = runtime.CreateAsync([||], t, dialect)

                let createdPath = currentPath @ [ t.Name ]
                let rebound =
                    createBackendBinding target [ t.Name ] createdPath response.Qid.Type response.Qid.Version response.Qid.Path

                withLock session.Gate (fun () ->
                    session.State <- ProtocolSessionOps.bindFid fid { rebound with IsOpened = true } session.State)

                return response :> obj
        }

    let dispatchCreateIntoBackendAsync
        (fid: uint32)
        (dialect: NinePDialect)
        (channel: Channel)
        (t: Tcreate)
        (session: SessionBox)
        : Task<obj> =
        task {
            match channel.Target with
            | NamespaceNode ->
                return raise (NinePProtocolException("Virtual namespace node"))
            | BackendNode(target, relativePath) ->
                let! runtime = materializeBackendRuntimeAsync target
                let! response = runtime.CreateAsync(relativePath |> List.toArray, t, dialect)

                let rebound =
                    createBackendBinding
                        target
                        (relativePath @ [ t.Name ])
                        (channel.InternalPath @ [ t.Name ])
                        response.Qid.Type
                        response.Qid.Version
                        response.Qid.Path

                withLock session.Gate (fun () ->
                    session.State <- ProtocolSessionOps.bindFid fid { rebound with IsOpened = true } session.State)

                return response :> obj
        }

    let tryResolveVirtualPathAsync (state: ProtocolSession) (dialect: NinePDialect) (pathState: PathState) =
        task {
            let fullPath = pathState.VisiblePath
            let ns = ProtocolSessionOps.namespaceOf state
            let key = NamespaceOps.mountKeyForPath fullPath
            let mountAtPath = NamespaceOps.findMount key ns

            let directAttachRoot =
                match mountAtPath with
                | Some chain ->
                    List.isEmpty fullPath
                    && List.isEmpty chain.MountPath
                    && ns.MountHash.Count = 1
                    && chain.Branches.Length = 1
                | None -> false

            let namespaceOwnedExactPath =
                mountAtPath.IsSome
                && not directAttachRoot
                && isVirtualDirectory ns fullPath

            match mountAtPath with
            | _ when namespaceOwnedExactPath ->
                let qid = syntheticDirectoryQid fullPath
                return Some(createVirtualBindingWithPathState dialect pathState qid.Type qid.Version qid.Path, qid)
            | Some chain when not (List.isEmpty chain.Branches) ->
                let target = chain.Branches.Head.Target
                let qid = syntheticDirectoryQid fullPath
                return Some(createBackendBindingWithPathState target [] pathState qid.Type qid.Version qid.Path, qid)
            | _ ->
                if isVirtualDirectory ns fullPath then
                    let qid = syntheticDirectoryQid fullPath
                    return Some(createVirtualBindingWithPathState dialect pathState qid.Type qid.Version qid.Path, qid)
                elif List.isEmpty fullPath then
                    return None
                else
                    let remoteMountPath = "/" + fullPath.Head
                    let! remoteExists = remoteMountExistsAsync remoteMountPath
                    if not remoteExists then
                        return None
                    else
                        let relative = if fullPath.Length > 1 then fullPath |> List.skip 1 else []
                        let descriptor = BackendTargetDescriptor.Remote(remoteMountPath, remoteMountPath)
                        let qid =
                            if List.isEmpty relative then syntheticDirectoryQid fullPath else syntheticFileQid fullPath
                        return Some(createBackendBindingWithPathState descriptor relative pathState qid.Type qid.Version qid.Path, qid)
        }

    let encodeVirtualReadEntries (dialect: NinePDialect) (currentPath: string list) (owner: string) (entriesToEncode: VirtualDirEntry array) =
        let entries = ResizeArray<byte>()
        for entry in entriesToEncode do
            let qid =
                match entry.QidType with
                | QidType.QTDIR -> syntheticDirectoryQid (currentPath @ [ entry.Name ])
                | _ -> syntheticFileQid (currentPath @ [ entry.Name ])

            let mode =
                match entry.QidType with
                | QidType.QTDIR -> 0755u ||| uint32 NinePConstants.FileMode9P.DMDIR
                | _ -> 0644u

            let stat =
                Stat(
                    0us,
                    0us,
                    0u,
                    qid,
                    mode,
                    0u,
                    0u,
                    0UL,
                    entry.Name,
                    owner,
                    owner,
                    owner,
                    dialect)
            let buffer = Array.zeroCreate<byte> (int stat.Size)
            let mutable offset = 0
            stat.WriteTo(buffer, &offset)
            entries.AddRange(buffer)
        entries.ToArray()

    let encodeVirtualReaddirEntries (currentPath: string list) (entriesToEncode: VirtualDirEntry array) =
        let buffer = ResizeArray<byte>()
        let mutable entryOffset = 0UL

        for entry in entriesToEncode do
            let qid =
                match entry.QidType with
                | QidType.QTDIR -> syntheticDirectoryQid (currentPath @ [ entry.Name ])
                | _ -> syntheticFileQid (currentPath @ [ entry.Name ])

            let nameBytes = Encoding.UTF8.GetBytes(entry.Name)
            let entrySize = 13UL + 8UL + 1UL + 2UL + uint64 nameBytes.Length
            let nextOffset = entryOffset + entrySize

            buffer.Add(byte qid.Type)
            buffer.AddRange(BitConverter.GetBytes(qid.Version))
            buffer.AddRange(BitConverter.GetBytes(qid.Path))
            buffer.AddRange(BitConverter.GetBytes(nextOffset))
            buffer.Add(if entry.QidType = QidType.QTDIR then 0x80uy else 0uy)
            buffer.AddRange(BitConverter.GetBytes(uint16 nameBytes.Length))
            buffer.AddRange(nameBytes)

            entryOffset <- nextOffset

        buffer.ToArray()

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

    let sliceVirtualReaddirData tag (offset: uint64) (count: uint32) (allData: byte array) =
        let page = ResizeArray<byte>()
        let mutable cursor = 0
        let mutable entryStartOffset = 0UL
        let mutable doneReading = false

        while cursor + 24 <= allData.Length && not doneReading do
            let nameLength = int (BitConverter.ToUInt16(allData, cursor + 22))
            let entrySize = 24 + nameLength
            if entrySize <= 0 || cursor + entrySize > allData.Length then
                doneReading <- true
            else
                if entryStartOffset >= offset then
                    if page.Count + entrySize > int count then
                        doneReading <- true
                    else
                        page.AddRange(allData.AsSpan(cursor, entrySize).ToArray())

                entryStartOffset <- BitConverter.ToUInt64(allData, cursor + 13)
                cursor <- cursor + entrySize

        Rreaddir((uint)(NinePConstants.HeaderSize + 4 + page.Count), tag, uint32 page.Count, page.ToArray()) :> obj

    let handleVirtualRead (tag: uint16) (dialect: NinePDialect) (currentPath: string list) (offset: uint64) (count: uint32) (ct: CancellationToken) (session: SessionBox) =
        task {
            ct.ThrowIfCancellationRequested()
            let state = getSessionStateOrThrow session
            let! entries = getVirtualChildEntriesAsync dialect state.Process.Namespace currentPath ct
            let allVirtualData = encodeVirtualReadEntries dialect currentPath state.UserName entries
            return sliceVirtualReadData tag offset count allVirtualData
        }

    let handleVirtualReaddir (tag: uint16) (dialect: NinePDialect) (currentPath: string list) (offset: uint64) (count: uint32) (ct: CancellationToken) (session: SessionBox) =
        task {
            ct.ThrowIfCancellationRequested()
            let state = getSessionStateOrThrow session
            let! entries = getVirtualChildEntriesAsync dialect state.Process.Namespace currentPath ct
            let allVirtualData = encodeVirtualReaddirEntries currentPath entries
            return sliceVirtualReaddirData tag offset count allVirtualData
        }

    /// Handle readdir for union mounts - iterates through all backends and dedupes
    let handleUnionReaddir (tag: uint16) (dialect: NinePDialect) (chain: MountChain) (offset: uint64) (count: uint32) (ct: CancellationToken) =
        task {
            ct.ThrowIfCancellationRequested()
            let! entries = getMountDirectoryEntriesAsync dialect chain.Branches ct
            let allVirtualData = encodeVirtualReaddirEntries chain.MountPath entries
            return sliceVirtualReaddirData tag offset count allVirtualData
        }

    let handleVirtualStat (tag: uint16) (dialect: NinePDialect) (currentPath: string list) (owner: string) =
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
                owner,
                owner,
                owner,
                dialect)

        Rstat(tag, stat) :> obj

    let handleVirtualOpen (tag: uint16) (currentPath: string list) =
        Ropen(tag, syntheticDirectoryQid currentPath, 0u) :> obj

    let handleAttach (t: Tattach) dialect (certificate: X509Certificate2) (session: SessionBox) : Task<obj> =
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
                let qid = syntheticDirectoryQid []
                let root = createVirtualBinding dialect [] qid.Type qid.Version qid.Path
                updateSessionState session (fun state ->
                    state
                    |> ProtocolSessionOps.withUserName t.Uname
                    |> ProtocolSessionOps.withNamespace ns
                    |> ProtocolSessionOps.withProcessRoot root)
                bindFid t.Fid root session
            else
                let! resolution = attachResolver.ResolveAsync(t.Aname, credentials, certificate)
                let target =
                    match resolution.Target with
                    | null -> raise (NinePProtocolException("Attach resolution did not produce a backend target"))
                    | value -> value

                let directNamespace =
                    let key = NamespaceOps.mountKeyForPath []
                    { MountHash =
                        Map.ofList
                            [ key,
                                { MountId = 1UL
                                  From = key
                                  MountPath = []
                                  Branches =
                                      [ { Target = target
                                          Flags = BindFlags.MREPL } ] } ] }
                let qid = syntheticDirectoryQid []
                let root = createBackendBinding target [] [] qid.Type qid.Version qid.Path
                updateSessionState session (fun state ->
                    state
                    |> ProtocolSessionOps.withUserName t.Uname
                    |> ProtocolSessionOps.withNamespace directNamespace
                    |> ProtocolSessionOps.withProcessRoot root)
                bindFid t.Fid root session
            return Rattach(t.Tag, Qid(QidType.QTDIR, 0u, 0UL)) :> obj
        }

    /// Walk one segment and check for mount crossing (9front domount semantics).
    /// Returns (channel with updated Mtpt, qid) or None if walk fails.
    let walkOneSegmentAsync (ns: Namespace) (dialect: NinePDialect) (segment: string) (chan: Channel) =
        task {
            // Apply segment to path state
            let tentative = ChannelOps.walk [ segment ] chan

            match tentative.Target with
            | NamespaceNode ->
                // Virtual namespace node - check if there's a mount at this channel
                // Use channel identity (Type, Dev, Qid) not path per 9front semantics
                let key = MountKeyModule.fromChannel tentative
                match NamespaceOps.findMount key ns with
                | Some chain when not (List.isEmpty chain.Branches) ->
                    // Mount found - cross into it
                    let target = chain.Branches.Head.Target
                    let! runtime = materializeBackendRuntimeAsync target
                    let! walkResult = runtime.WalkAsync([||], dialect)
                    let qid =
                        if isNull walkResult.Wqid || walkResult.Wqid.Length = 0 then
                            syntheticDirectoryQid tentative.InternalPath
                        else
                            walkResult.Wqid.[0]
                    let newPathState =
                        { tentative.PathState with Mtpt = chan :: tentative.PathState.Mtpt }
                    let crossed =
                        { ChannelOps.createBackendNodeWithPathState
                            { Type = qid.Type; Version = qid.Version; Path = qid.Path }
                            target
                            []
                            newPathState
                          with Umh = if chain.Branches.Length > 1 then Some chain else None }
                    return Some (crossed, qid)
                | _ ->
                    // No mount at this qid - check if path exists in namespace
                    // A namespace directory exists if it has mounted children
                    if hasMountedChildren ns tentative.InternalPath then
                        let qid = syntheticDirectoryQid tentative.InternalPath
                        return Some (tentative, qid)
                    else
                        return None  // Path doesn't exist in namespace

            | BackendNode(target, relativePath) ->
                // Already in a backend - walk one segment
                // Walk the full accumulated path since runtime may not track state
                let nextRelativePath = relativePath @ [ segment ]
                let! runtime = materializeBackendRuntimeAsync target
                try
                    let! walkResult = runtime.WalkAsync(nextRelativePath |> List.toArray, dialect)
                    if isNull walkResult.Wqid || walkResult.Wqid.Length <> nextRelativePath.Length then
                        return None
                    else
                        let qid = walkResult.Wqid.[nextRelativePath.Length - 1]
                        let walkedChan =
                            ChannelOps.createBackendNodeWithPathState
                                { Type = qid.Type; Version = qid.Version; Path = qid.Path }
                                target
                                nextRelativePath
                                tentative.PathState
                        // Check for mount at this qid (nested mount)
                        let key = MountKeyModule.fromChannel walkedChan
                        match NamespaceOps.findMount key ns with
                        | Some chain when not (List.isEmpty chain.Branches) ->
                            // Mount found at this qid - cross into it
                            let mountTarget = chain.Branches.Head.Target
                            let! mountRuntime = materializeBackendRuntimeAsync mountTarget
                            let! mountWalk = mountRuntime.WalkAsync([||], dialect)
                            let mountQid =
                                if isNull mountWalk.Wqid || mountWalk.Wqid.Length = 0 then qid
                                else mountWalk.Wqid.[0]
                            let newPathState =
                                { walkedChan.PathState with Mtpt = walkedChan :: walkedChan.PathState.Mtpt }
                            let crossed =
                                ChannelOps.createBackendNodeWithPathState
                                    { Type = mountQid.Type; Version = mountQid.Version; Path = mountQid.Path }
                                    mountTarget
                                    []
                                    newPathState
                            return Some (crossed, mountQid)
                        | _ ->
                            return Some (walkedChan, qid)
                with
                | :? NinePProtocolException -> return None
        }

    let handleWalk (t: Twalk) (dialect: NinePDialect) (session: SessionBox) : Task<obj> =
        task {
            let sourceChannel : Channel =
                withLock session.Gate (fun () ->
                    match ProtocolSessionOps.tryFindFid t.Fid session.State with
                    | Some channel when t.NewFid <> t.Fid && ProtocolSessionOps.containsFid t.NewFid session.State ->
                        raise (NinePProtocolException(sprintf "newfid %u already exists" t.NewFid))
                    | Some channel -> channel
                    | None -> raise (NinePProtocolException("Unknown FID")))

            let stateSnapshot = getSessionStateOrThrow session
            let ns = ProtocolSessionOps.namespaceOf stateSnapshot
            let qids = ResizeArray<NinePSharp.Constants.Qid>()
            let mutable lastChannel = sourceChannel
            let mutable failed = false

            if t.Wname.Length = 0 then
                lastChannel <- sourceChannel
            else
                for segment in t.Wname do
                    if not failed then
                        let! result = walkOneSegmentAsync ns dialect segment lastChannel
                        match result with
                        | Some (channel, qid) ->
                            qids.Add(qid)
                            lastChannel <- channel
                        | None ->
                            failed <- true

            let mutable updateFailure : obj option = None

            if t.Wname.Length = 0 || qids.Count > 0 then
                updateFailure <-
                    withLock session.Gate (fun () ->
                        match ProtocolSessionOps.tryFindFid t.Fid session.State with
                        | None ->
                            Some (Rerror(t.Tag, $"fid {t.Fid} was removed during walk") :> obj)
                        | Some _ when t.NewFid <> t.Fid && ProtocolSessionOps.containsFid t.NewFid session.State ->
                            Some (Rerror(t.Tag, $"newfid {t.NewFid} was claimed by another thread") :> obj)
                        | Some _ ->
                            session.State <- ProtocolSessionOps.bindFid t.NewFid lastChannel session.State
                            None)

            match updateFailure with
            | Some errorResponse -> return errorResponse
            | None when failed && qids.Count = 0 ->
                return raise (NinePProtocolException("walk failed"))
            | None -> return Rwalk(t.Tag, if qids.Count = 0 then null else qids.ToArray()) :> obj
        }

    let handleWrite (t: Twrite) dialect (ct: CancellationToken) (session: SessionBox) : Task<obj> =
        task {
            ct.ThrowIfCancellationRequested()
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
                requireOpened "write" channel
                return!
                    dispatchWithChannelAsync dialect channel (fun (runtime, relativePath) ->
                        task {
                            let! response = runtime.WriteAsync(relativePath, t, dialect, ct)
                            updateChannelOffset t.Fid (t.Offset + uint64 response.Count) session
                            return response :> obj
                        })
        }

    let handleClunk (t: Tclunk) (session: SessionBox) : Task<obj> =
        task {
            let mutable authRemoved = false
            let mutable removedChannel = Unchecked.defaultof<Channel>
            let mutable hadChannel = false

            withLock session.Gate (fun () ->
                match ProtocolSessionOps.tryFindAuthFid t.Fid session.State with
                | Some secure ->
                    secure.Dispose()
                    authRemoved <- true
                    session.State <- ProtocolSessionOps.removeAuthFid t.Fid session.State
                | None -> ()

                match ProtocolSessionOps.tryFindFid t.Fid session.State with
                | Some channel ->
                    removedChannel <- channel
                    hadChannel <- true
                    session.State <- ProtocolSessionOps.removeFid t.Fid session.State
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

    let handleRemove (t: Tremove) (dialect: NinePDialect) (session: SessionBox) : Task<obj> =
        task {
            let mutable removedChannel = Unchecked.defaultof<Channel>
            let mutable hadChannel = false

            withLock session.Gate (fun () ->
                match ProtocolSessionOps.tryFindFid t.Fid session.State with
                | Some channel ->
                    removedChannel <- channel
                    hadChannel <- true
                    session.State <- ProtocolSessionOps.removeFid t.Fid session.State
                | None -> ())

            if not hadChannel then
                raise (NinePProtocolException("Unknown FID"))

            // Check if this is a mount point by looking up the path-based key
            let stateSnapshot = getSessionStateOrThrow session
            let ns = ProtocolSessionOps.namespaceOf stateSnapshot
            let pathKey = NamespaceOps.mountKeyForPath removedChannel.InternalPath
            let mountAtPath = NamespaceOps.findMount pathKey ns

            match removedChannel.Target, mountAtPath with
            | NamespaceNode, Some chain ->
                // This is an unmount operation (removing a namespace binding)
                updateSessionState session (ProtocolSessionOps.withNamespace (NamespaceOps.unmountByMountId chain.MountId ns))
                return Rremove(t.Tag) :> obj
            | BackendNode(_, relativePath), Some chain when List.isEmpty relativePath ->
                // At mount root - treat as unmount
                updateSessionState session (ProtocolSessionOps.withNamespace (NamespaceOps.unmountByMountId chain.MountId ns))
                return Rremove(t.Tag) :> obj
            | NamespaceNode, None ->
                return raise (NinePNotSupportedException())
            | BackendNode _, _ ->
                return!
                    dispatchWithChannelAsync dialect removedChannel (fun (runtime, relativePath) ->
                        task {
                            let! response = runtime.RemoveAsync(relativePath, t, dialect)
                            return response :> obj
                        })
        }

    interface INinePFSDispatcher with
        member _.DispatchAsync(sessionId, message, dialect, certificate) : Task<obj> =
            task {
                let tag = getTag message
                let session = getOrCreateSessionBox sessionId dialect certificate

                try
                    match message with
                    | NinePMessage.MsgTversion t ->
                        return Rversion(t.Tag, t.MSize, "9P2000") :> obj

                    | NinePMessage.MsgTauth t ->
                        return! withFidLocks session [ t.Afid ] (fun () ->
                            task {
                                let secure = new SecureString()
                                withLock session.Gate (fun () ->
                                    session.State <- ProtocolSessionOps.addAuthFid t.Afid secure session.State)
                                return Rauth(t.Tag, Qid(QidType.QTAUTH, 0u, uint64 t.Afid)) :> obj
                            })

                    | NinePMessage.MsgTflush t ->
                        // 9front flush semantics: cancel in-flight request and wait for completion
                        match session.InFlightRequests.TryGetValue(t.OldTag) with
                        | true, inFlight ->
                            // Cancel the request
                            try inFlight.Cts.Cancel() with _ -> ()
                            // Wait for it to complete (9front delays flush response until original completes)
                            try
                                do! inFlight.Completion.Task
                            with _ -> ()
                            return Rflush(t.Tag) :> obj
                        | false, _ ->
                            // Request not found or already completed
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
                                    return handleVirtualStat t.Tag dialect channel.InternalPath (getSessionUserName session)
                                else
                                    return! dispatchWithChannelAsync dialect channel (fun (runtime, relativePath) ->
                                        task {
                                            let! response = runtime.StatAsync(relativePath, t, dialect)
                                            return response :> obj
                                        })
                            })

                    | NinePMessage.MsgTcreate t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                if isNamespaceChannel channel then
                                    // Re-resolve with Create intent to find correct parent branch
                                    let stateSnapshot = getSessionStateOrThrow session
                                    let! resolved = tryResolveVirtualPathAsync stateSnapshot dialect channel.PathState
                                    match resolved with
                                    | Some (updChannel, _) ->
                                        return! dispatchCreateIntoNamespaceAsync t.Tag t.Fid dialect updChannel t session
                                    | None ->
                                        return! dispatchCreateIntoNamespaceAsync t.Tag t.Fid dialect channel t session
                                else
                                    return! dispatchCreateIntoBackendAsync t.Fid dialect channel t session
                            })

                    | NinePMessage.MsgTwstat t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            let channel = getChannelOrThrow t.Fid session
                            dispatchWithChannelAsync dialect channel (fun (runtime, relativePath) ->
                                task {
                                    let! response = runtime.WstatAsync(relativePath, t, dialect)
                                    return response :> obj
                                }))

                    | NinePMessage.MsgTopen t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                if isNamespaceChannel channel then
                                    // Re-resolve with Open intent to handle union semantics correctly
                                    let stateSnapshot = getSessionStateOrThrow session
                                    let! resolved = tryResolveVirtualPathAsync stateSnapshot dialect channel.PathState
                                    match resolved with
                                    | Some (updChannel, _) ->
                                        bindFid t.Fid updChannel session
                                        markChannelOpened t.Fid None session
                                        return handleVirtualOpen t.Tag updChannel.InternalPath
                                    | None ->
                                        markChannelOpened t.Fid None session
                                        return handleVirtualOpen t.Tag channel.InternalPath
                                else
                                    return! dispatchWithChannelAsync dialect channel (fun (runtime, relativePath) ->
                                        task {
                                            let! response = runtime.OpenAsync(relativePath, t, dialect)
                                            markChannelOpened t.Fid (Some response.Qid) session
                                            return response :> obj
                                        })
                            })

                    | NinePMessage.MsgTread t ->
                        return! withInFlightTracking t.Tag session (fun ct ->
                            withFidLocks session [ t.Fid ] (fun () ->
                                task {
                                    let channel = getChannelOrThrow t.Fid session
                                    requireOpened "read" channel
                                    if isNamespaceChannel channel then
                                        return! handleVirtualRead t.Tag dialect channel.InternalPath t.Offset t.Count ct session
                                    else
                                        return! dispatchWithChannelAsync dialect channel (fun (runtime, relativePath) ->
                                            task {
                                                let! response = runtime.ReadAsync(relativePath, t, dialect, ct)
                                                updateChannelOffset t.Fid (t.Offset + uint64 response.Count) session
                                                return response :> obj
                                            })
                                }))

                    | NinePMessage.MsgTwrite t ->
                        return! withInFlightTracking t.Tag session (fun ct ->
                            withFidLocks session [ t.Fid ] (fun () -> handleWrite t dialect ct session))

                    | NinePMessage.MsgTreaddir t ->
                        return! withInFlightTracking t.Tag session (fun ct ->
                            withFidLocks session [ t.Fid ] (fun () ->
                                task {
                                    let channel = getChannelOrThrow t.Fid session
                                    requireOpened "readdir" channel
                                    if isNamespaceChannel channel then
                                        return! handleVirtualReaddir t.Tag dialect channel.InternalPath t.Offset t.Count ct session
                                    else
                                        // Check for union mount (multiple backends)
                                        match channel.Umh with
                                        | Some chain ->
                                            // Union readdir - iterate through all backends
                                            return! handleUnionReaddir t.Tag dialect chain t.Offset t.Count ct
                                        | None ->
                                            // Single backend - dispatch directly
                                            return! dispatchWithChannelAsync dialect channel (fun (runtime, relativePath) ->
                                                task {
                                                    let! response = runtime.ReaddirCompatAsync(relativePath, t, dialect, ct)
                                                    return response :> obj
                                                })
                                }))

                    | _ ->
                        return raise (NinePProtocolException("Message type not implemented or supported"))
                with
                | :? NinePProtocolException as ex ->
                    return createErrorResponse tag dialect ex
                | _ ->
                    return createErrorResponse tag dialect (NinePProtocolException("Internal Server Error"))
            }
