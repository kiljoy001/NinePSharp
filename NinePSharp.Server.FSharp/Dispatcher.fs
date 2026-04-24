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
open NinePSharp.Server.FileSystem

/// Tracks an in-flight request for flush support.
type private InFlightRequest =
    { Tag: uint16
      Cts: CancellationTokenSource
      Completion: TaskCompletionSource<unit> }

type private SessionBox(state: ProtocolSession) =
    let gate = obj()
    let inFlightRequests = ConcurrentDictionary<uint16, InFlightRequest>()
    let authHandlers = ConcurrentDictionary<uint32, IAuthHandler>()
    member _.Gate = gate
    member val State = state with get, set
    member _.InFlightRequests = inFlightRequests
    member _.AuthHandlers = authHandlers

type NinePFSDispatcherEngine(handler: INinePRequestHandler) =
    let sessions = ConcurrentDictionary<string, SessionBox>(StringComparer.Ordinal)
    let fidOperationGates = ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal)

    let withLock (gate: obj) (action: unit -> 'T) : 'T =
        lock gate action

    let createErrorResponse tag dialect (error: Exception) : obj =
        if dialect = NinePDialect.NineP2000L then
            Rlerror(tag, 22u) :> obj // EINVAL fallback
        else
            Rerror(tag, error.Message) :> obj

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
        | NinePMessage.MsgTsymlink t -> t.Tag
        | NinePMessage.MsgTreadlink t -> t.Tag
        | NinePMessage.MsgTlink t -> t.Tag
        | NinePMessage.MsgTlock t -> t.Tag
        | NinePMessage.MsgTgetlock t -> t.Tag
        | NinePMessage.MsgTxattrwalk t -> t.Tag
        | NinePMessage.MsgTxattrcreate t -> t.Tag
        | _ -> 0us

    let getOrCreateSessionBox (sessionId: string) dialect (certificate: X509Certificate2) : SessionBox =
        let sessionBox =
            sessions.GetOrAdd(
                sessionId,
                Func<string, SessionBox>(fun id -> 
                    SessionBox(ProtocolSessionOps.create id dialect certificate)))
        sessionBox

    let bindFid (fid: uint32) (channel: Channel) (session: SessionBox) : unit =
        withLock session.Gate (fun () -> session.State <- ProtocolSessionOps.bindFid fid channel session.State)

    let withInFlightTracking (tag: uint16) (session: SessionBox) (action: CancellationToken -> Task<obj>) : Task<obj> =
        task {
            let cts = new CancellationTokenSource()
            let completion = TaskCompletionSource<unit>()
            let inFlight = { Tag = tag; Cts = cts; Completion = completion }
            session.InFlightRequests.[tag] <- inFlight
            try
                return! action cts.Token
            finally
                completion.TrySetResult(()) |> ignore
                session.InFlightRequests.TryRemove(tag) |> ignore
                cts.Dispose()
        }

    let getChannelOrThrow (fid: uint32) (session: SessionBox) : Channel =
        withLock session.Gate (fun () ->
            match ProtocolSessionOps.tryFindFid fid session.State with
            | Some channel -> channel
            | None -> raise (Exception("Unknown FID")))

    let gateKey (session: SessionBox) (fid: uint32) = $"{session.State.SessionId}:{fid}"
    let getOperationGate (session: SessionBox) (fid: uint32) =
        fidOperationGates.GetOrAdd(gateKey session fid, fun _ -> new SemaphoreSlim(1, 1))

    let withFidLocks (session: SessionBox) (fidsToLock: seq<uint32>) (action: unit -> Task<obj>) : Task<obj> =
        task {
            let distinctFids = fidsToLock |> Seq.distinct |> Seq.sort |> Seq.toArray
            let gates = distinctFids |> Array.map (getOperationGate session)
            for gate in gates do do! gate.WaitAsync()
            try return! action()
            finally for i = gates.Length - 1 downto 0 do gates.[i].Release() |> ignore
        }

    interface INinePFSDispatcher with
        member _.DispatchAsync(sessionId, message, dialect, certificate) : Task<obj> =
            task {
                let tag = getTag message
                let session = getOrCreateSessionBox sessionId dialect certificate

                try
                    match message with
                    | NinePMessage.MsgTversion t ->
                        return Rversion(t.Tag, t.MSize, t.Version) :> obj

                    | NinePMessage.MsgTauth t ->
                        return! withFidLocks session [ t.Afid ] (fun () ->
                            task {
                                let! authHandler = handler.GetAuthHandlerAsync(t, CancellationToken.None)
                                if box authHandler <> null then
                                    session.AuthHandlers.[t.Afid] <- authHandler
                                return Rauth(t.Tag, Qid(QidType.QTAUTH, 0u, uint64 t.Afid)) :> obj
                            })

                    | NinePMessage.MsgTattach t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let! rattach = handler.AttachAsync(t, CancellationToken.None)
                                let rootChan = ProtocolSessionOps.createBinding (BackendNode []) rattach.Qid.Type rattach.Qid.Version rattach.Qid.Path []
                                bindFid t.Fid rootChan session
                                return rattach :> obj
                            })

                    | NinePMessage.MsgTwalk t ->
                        return! withFidLocks session [ t.Fid; t.NewFid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                let relPath = match channel.Target with | BackendNode p -> p | _ -> []
                                let! rwalk = handler.WalkAsync(relPath |> List.toArray, t, CancellationToken.None)
                                
                                if box rwalk = null then
                                    return raise (Exception("Walk failed")) :> obj
                                else
                                    if t.Wname.Length > 0 && (isNull rwalk.Wqid || rwalk.Wqid.Length = 0) then
                                        return raise (Exception("File not found")) :> obj
                                    else
                                        if t.Wname.Length = 0 || (not (isNull rwalk.Wqid) && rwalk.Wqid.Length = t.Wname.Length) then
                                            let mutable currentPath = relPath
                                            let count = if isNull rwalk.Wqid then 0 else rwalk.Wqid.Length
                                            for i in 0 .. count - 1 do
                                                currentPath <- currentPath @ [ t.Wname.[i] ]
                                            
                                            let finalQid = if count = 0 then channel.Qid else { Type = rwalk.Wqid.[count - 1].Type; Version = rwalk.Wqid.[count - 1].Version; Path = rwalk.Wqid.[count - 1].Path }
                                            let walkedChan = ProtocolSessionOps.createBinding (BackendNode currentPath) finalQid.Type finalQid.Version finalQid.Path currentPath
                                            bindFid t.NewFid walkedChan session
                                        
                                        return rwalk :> obj
                            })

                    | NinePMessage.MsgTopen t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                let relPath = match channel.Target with | BackendNode p -> p | _ -> []
                                let! ropen = handler.OpenAsync(relPath |> List.toArray, t, CancellationToken.None)
                                let openedChan = { channel with IsOpened = true; Qid = { Type = ropen.Qid.Type; Version = ropen.Qid.Version; Path = ropen.Qid.Path } }
                                bindFid t.Fid openedChan session
                                return ropen :> obj
                            })

                    | NinePMessage.MsgTread t ->
                        return! withInFlightTracking t.Tag session (fun ct ->
                            withFidLocks session [ t.Fid ] (fun () ->
                                task {
                                    if session.AuthHandlers.ContainsKey(t.Fid) then
                                        let auth = session.AuthHandlers.[t.Fid]
                                        let! data = auth.ReadAsync(t.Offset, t.Count, ct)
                                        return Rread(t.Tag, data) :> obj
                                    else
                                        let channel = getChannelOrThrow t.Fid session
                                        let relPath = match channel.Target with | BackendNode p -> p | _ -> []
                                        let! rread = handler.ReadAsync(relPath |> List.toArray, t, ct)
                                        return rread :> obj
                                }))

                    | NinePMessage.MsgTwrite t ->
                        return! withInFlightTracking t.Tag session (fun ct ->
                            withFidLocks session [ t.Fid ] (fun () ->
                                task {
                                    if session.AuthHandlers.ContainsKey(t.Fid) then
                                        let auth = session.AuthHandlers.[t.Fid]
                                        let! count = auth.WriteAsync(t.Offset, t.Data.ToArray(), ct)
                                        return Rwrite(t.Tag, count) :> obj
                                    else
                                        let channel = getChannelOrThrow t.Fid session
                                        let relPath = match channel.Target with | BackendNode p -> p | _ -> []
                                        let! rwrite = handler.WriteAsync(relPath |> List.toArray, t, ct)
                                        return rwrite :> obj
                                }))

                    | NinePMessage.MsgTclunk t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let mutable auth = null
                                if session.AuthHandlers.TryRemove(t.Fid, &auth) then
                                    return Rclunk(t.Tag) :> obj
                                else
                                    let channel = getChannelOrThrow t.Fid session
                                    let relPath = match channel.Target with | BackendNode p -> p | _ -> []
                                    let! rclunk = handler.ClunkAsync(relPath |> List.toArray, t, CancellationToken.None)
                                    withLock session.Gate (fun () -> session.State <- ProtocolSessionOps.removeFid t.Fid session.State)
                                    return rclunk :> obj
                            })

                    | NinePMessage.MsgTflush t ->
                        match session.InFlightRequests.TryGetValue(t.OldTag) with
                        | true, inFlight ->
                            inFlight.Cts.Cancel()
                            do! inFlight.Completion.Task
                            return Rflush(t.Tag) :> obj
                        | false, _ ->
                            return Rflush(t.Tag) :> obj

                    | NinePMessage.MsgTstat t ->
                         return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                let relPath = match channel.Target with | BackendNode p -> p | _ -> []
                                let! rstat = handler.StatAsync(relPath |> List.toArray, t, CancellationToken.None)
                                return rstat :> obj
                            })

                    | NinePMessage.MsgTreaddir t ->
                        return! withInFlightTracking t.Tag session (fun ct ->
                            withFidLocks session [ t.Fid ] (fun () ->
                                task {
                                    let channel = getChannelOrThrow t.Fid session
                                    if not channel.IsOpened then
                                        return raise (Exception("Fid not opened")) :> obj
                                    else
                                        let relPath = match channel.Target with | BackendNode p -> p | _ -> []
                                        let task = handler.ReaddirAsync(relPath |> List.toArray, t, ct)
                                        if box task = null then
                                            return Rreaddir((uint)(NinePConstants.HeaderSize + 4), t.Tag, 0u, ReadOnlyMemory.Empty) :> obj
                                        else
                                            let! result = task
                                            if box result = null then
                                                return Rreaddir((uint)(NinePConstants.HeaderSize + 4), t.Tag, 0u, ReadOnlyMemory.Empty) :> obj
                                            else
                                                return result :> obj
                                }))

                    | NinePMessage.MsgTcreate t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                let relPath = match channel.Target with | BackendNode p -> p | _ -> []
                                let! rcreate = handler.CreateAsync(relPath |> List.toArray, t, CancellationToken.None)
                                let newPath = relPath @ [ t.Name ]
                                let createdChan = ProtocolSessionOps.createBinding (BackendNode newPath) rcreate.Qid.Type rcreate.Qid.Version rcreate.Qid.Path newPath
                                bindFid t.Fid { createdChan with IsOpened = true } session
                                return rcreate :> obj
                            })

                    | NinePMessage.MsgTremove t ->
                        return! withFidLocks session [ t.Fid ] (fun () ->
                            task {
                                let channel = getChannelOrThrow t.Fid session
                                let relPath = match channel.Target with | BackendNode p -> p | _ -> []
                                let! rremove = handler.RemoveAsync(relPath |> List.toArray, t, CancellationToken.None)
                                withLock session.Gate (fun () -> session.State <- ProtocolSessionOps.removeFid t.Fid session.State)
                                return rremove :> obj
                            })

                    | _ ->
                        return raise (Exception("Message type not implemented"))
                with
                | ex ->
                    return createErrorResponse tag dialect ex
            }
