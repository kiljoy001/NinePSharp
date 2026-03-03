import re
import sys

with open("NinePSharp.Server.FSharp/Dispatcher.fs", "r") as f:
    text = f.read()

# Replace constructor
text = text.replace("type NinePFSDispatcherEngine(attachResolver: IAttachResolver) =", "type NinePFSDispatcherEngine(handler: INinePRequestHandler) =")

# Fix createBackendBinding
text = re.sub(r"let createBackendBinding \(target: BackendTargetDescriptor\) \(relativePath: string list\) \(visiblePath: string list\) \(qidType: QidType\) \(qidVersion: uint32\) \(qidPath: uint64\) =\s*ChannelOps.createBackendNode \{ Type = qidType; Version = qidVersion; Path = qidPath \} target relativePath visiblePath",
              r"let createBackendBinding (relativePath: string list) (visiblePath: string list) (qidType: QidType) (qidVersion: uint32) (qidPath: uint64) =\n        ChannelOps.createBackendNode { Type = qidType; Version = qidVersion; Path = qidPath } relativePath visiblePath", text)

# Remove buildRootNamespace block
text = re.sub(r"let buildRootNamespace \(certificate: X509Certificate2\) =.*?{ MountHash = chains \|> Map.ofList }", 
              r"let buildRootNamespace (certificate: X509Certificate2) = NamespaceOps.empty", 
              text, flags=re.DOTALL)

# Delete materializeBackendRuntimeAsync block
text = re.sub(r"let materializeBackendRuntimeAsync \(target: BackendTargetDescriptor\) =.*?return target.CreateRuntime\(\)\s*\}", 
              "", text, flags=re.DOTALL)

# Remove getVirtualChildEntriesAsync, since we don't have remoteMountPaths anymore
text = re.sub(r"let getVirtualChildEntriesAsync.*?return entries.ToArray\(\)\s*\}", 
              "let getVirtualChildEntriesAsync (dialect: NinePDialect) (ns: Namespace) (channel: Channel) (ct: System.Threading.CancellationToken) = task {\n            return [||]\n        }", text, flags=re.DOTALL)


# Fix getMountDirectoryEntriesAsync
text = re.sub(r"let getMountDirectoryEntriesAsync \(dialect: NinePDialect\) \(branches: MountBranch list\) \(ct: CancellationToken\) =.*?for branch in branches do.*?let! runtime = materializeBackendRuntimeAsync branch.Target.*?let! page = runtime.ReadAsync\(\[\|\|\], Tread\(0us, 0u, 0UL, UInt32.MaxValue\), dialect, ct\).*?return entries.ToArray\(\)\s*\}", 
              r"let getMountDirectoryEntriesAsync (dialect: NinePDialect) (branches: MountBranch list) (ct: CancellationToken) = task {\n            return [||]\n        }", text, flags=re.DOTALL)

# Fix tryResolveBranchPathAsync
text = re.sub(r"let tryResolveBranchPathAsync.*?return resolved\s*\}", 
              r"let tryResolveBranchPathAsync (dialect: NinePDialect) (branches: MountBranch list) (remainder: string list) = task { return None }", text, flags=re.DOTALL)

# Fix dispatchWithChannelAsync to use `handler` and `BackendNode(relativePath)`
text = re.sub(r"let dispatchWithChannelAsync\s*\(dialect: NinePDialect\)\s*\(channel: Channel\)\s*\(action: IBackendRuntime \* string array -> Task<obj>\)\s*: Task<obj> =\s*task \{\s*match channel.Target with\s*\| NamespaceNode ->\s*return raise \(NinePProtocolException\(\"Virtual namespace node\"\)\)\s*\| BackendNode\(target, relativePath\) ->\s*let! runtime = materializeBackendRuntimeAsync target\s*return! action \(runtime, relativePath \|> List.toArray\)\s*\}",
              r"""let dispatchWithChannelAsync
        (dialect: NinePDialect)
        (channel: Channel)
        (action: INinePRequestHandler * string array -> Task<obj>)
        : Task<obj> =
        task {
            match channel.Target with
            | NamespaceNode ->
                return raise (NinePProtocolException("Virtual namespace node"))
            | BackendNode(relativePath) ->
                return! action (handler, relativePath |> List.toArray)
        }""", text)

# Now fix dispatchCreateIntoNamespaceAsync to just fail or use root handler
text = re.sub(r"let dispatchCreateIntoNamespaceAsync.*?: Task<obj> =\s*task \{.*?return response :> obj\s*\}",
              r"""let dispatchCreateIntoNamespaceAsync (tag: uint16) (fid: uint32) (dialect: NinePDialect) (channel: Channel) (t: Tcreate) (session: SessionBox) (ct: CancellationToken) : Task<obj> = task { return raise (NinePProtocolException("Namespace writes not supported")) }""", text, flags=re.DOTALL)

# Now fix dispatchCreateIntoBackendAsync
text = re.sub(r"let dispatchCreateIntoBackendAsync\s*\(fid: uint32\)\s*\(dialect: NinePDialect\)\s*\(channel: Channel\)\s*\(t: Tcreate\)\s*\(session: SessionBox\)\s*\(ct: CancellationToken\)\s*: Task<obj> =\s*task \{\s*ct.ThrowIfCancellationRequested\(\)\s*match channel.Target with\s*\| NamespaceNode ->\s*return raise \(NinePProtocolException\(\"Virtual namespace node\"\)\)\s*\| BackendNode\(target, relativePath\) ->\s*let! runtime = materializeBackendRuntimeAsync target\s*let! response = runtime.CreateAsync\(relativePath \|> List.toArray, t, dialect, ct\)\s*let rebound =\s*createBackendBinding\s*target\s*\(relativePath @ \[ t.Name \]\)\s*\(channel.InternalPath @ \[ t.Name \]\)\s*response.Qid.Type\s*response.Qid.Version\s*response.Qid.Path.*?:> obj\s*\}",
              r"""let dispatchCreateIntoBackendAsync (fid: uint32) (dialect: NinePDialect) (channel: Channel) (t: Tcreate) (session: SessionBox) (ct: CancellationToken) : Task<obj> =
        task {
            ct.ThrowIfCancellationRequested()
            match channel.Target with
            | NamespaceNode -> return raise (NinePProtocolException("Virtual namespace node"))
            | BackendNode(relativePath) ->
                let! response = handler.CreateAsync(relativePath |> List.toArray, t, ct)
                let rebound = createBackendBinding (relativePath @ [ t.Name ]) (channel.InternalPath @ [ t.Name ]) response.Qid.Type response.Qid.Version response.Qid.Path
                withLock session.Gate (fun () -> session.State <- ProtocolSessionOps.bindFid fid { rebound with IsOpened = true } session.State)
                return response :> obj
        }""", text, flags=re.DOTALL)

# Fix Tattach logic (remove attachResolver.ResolveAttachAsync)
text = re.sub(r"let! resolution = attachResolver.ResolveAttachAsync\(t.Aname, t.Uname, certificate\).*?return Rattach\(t.Tag, Qid\(QidType.QTDIR, 0u, 0UL\)\) :> obj",
              r"""let rootQid = NinePSharp.Constants.Qid(QidType.QTDIR, 0u, 0UL)
                let root = createBackendBinding [] [] rootQid.Type rootQid.Version rootQid.Path
                updateSessionState session (fun state ->
                    state
                    |> ProtocolSessionOps.withUserName t.Uname
                    |> ProtocolSessionOps.withNamespace NamespaceOps.empty
                    |> ProtocolSessionOps.withProcessRoot root)
                bindFid t.Fid root session
            return Rattach(t.Tag, Qid(QidType.QTDIR, 0u, 0UL)) :> obj""", text, flags=re.DOTALL)

# Fix walkOneSegmentAsync matching
text = re.sub(r"for branch in chain.Branches do.*?let! runtime = materializeBackendRuntimeAsync branch.Target.*?try.*?let! walkResult = runtime.WalkAsync\(\[\|\|\], dialect\).*?target.*?with _ -> \(\).*?match crossResult with",
              r"""match findMount key ns with | Some _ -> () | None -> ()
                    match None with""", text, flags=re.DOTALL)

text = re.sub(r"\| BackendNode\(target, relativePath\) ->", r"| BackendNode(relativePath) ->", text)
text = re.sub(r"let! runtime = materializeBackendRuntimeAsync target", r"", text)
text = re.sub(r"runtime.WalkAsync\(parentRelativePath \|> List.toArray, dialect\)", r"handler.WalkAsync(parentRelativePath |> List.toArray, Twalk(0us, 0u, 0u, [||]), System.Threading.CancellationToken.None)", text) # This is a hack, WalkAsync actually takes msg and ct, but `walkOneSegmentAsync` shouldn't even exist in this form!
text = re.sub(r"runtime.WalkAsync\(nextRelativePath \|> List.toArray, dialect\)", r"handler.WalkAsync(nextRelativePath |> List.toArray, Twalk(0us, 0u, 0u, [||]), System.Threading.CancellationToken.None)", text)
text = re.sub(r"ChannelOps.createBackendNodeWithPathState.*?target.*?parentRelativePath", r"ChannelOps.createBackendNodeWithPathState { Type = qid.Type; Version = qid.Version; Path = qid.Path } parentRelativePath", text, flags=re.DOTALL)
text = re.sub(r"ChannelOps.createBackendNodeWithPathState.*?target.*?nextRelativePath", r"ChannelOps.createBackendNodeWithPathState { Type = qid.Type; Version = qid.Version; Path = qid.Path } nextRelativePath", text, flags=re.DOTALL)

# Fix dispatch operations (RemoveAsync)
text = re.sub(r"runtime.RemoveAsync\(relativePath, t, dialect, ct\)", r"handler.RemoveAsync(relativePath, t, ct)", text)

with open("NinePSharp.Server.FSharp/Dispatcher_Rewrite.fs", "w") as f:
    f.write(text)
