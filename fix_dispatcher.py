import re

with open("NinePSharp.Server.FSharp/Dispatcher.fs", "r") as f:
    text = f.read()

# Replace handleAttach
old_handle_attach = re.search(r"let handleAttach \(t: Tattach\) dialect certificate session : Task<obj> =\s*task \{.*?return Rattach\(t.Tag, Qid\(QidType.QTDIR, 0u, 0UL\)\) :> obj\s*\}", text, flags=re.DOTALL)
if old_handle_attach:
    new_handle_attach = """let handleAttach (t: Tattach) dialect certificate session : Task<obj> =
        task {
            let mutable credentials : byte array = null
            match ProtocolSessionOps.tryFindAuthFid t.Afid session.State with
            | Some secure when secure.Length > 0 ->
                if not (secure.IsReadOnly()) then
                    secure.MakeReadOnly()
                credentials <- Encoding.UTF8.GetBytes(System.Runtime.InteropServices.Marshal.PtrToStringUni(System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(secure)))
            | Some secure ->
                secure.Dispose()
            | None -> ()

            let rootQid = NinePSharp.Constants.Qid(QidType.QTDIR, 0u, 0UL)
            let root = ChannelOps.createBackendNode { Type = rootQid.Type; Version = rootQid.Version; Path = rootQid.Path } [] []
            updateSessionState session (fun state ->
                state
                |> ProtocolSessionOps.withUserName t.Uname
                |> ProtocolSessionOps.withNamespace NamespaceOps.empty
                |> ProtocolSessionOps.withProcessRoot root)
            bindFid t.Fid root session
            return Rattach(t.Tag, rootQid) :> obj
        }"""
    text = text[:old_handle_attach.start()] + new_handle_attach + text[old_handle_attach.end():]

# Remove bindAsync
text = re.sub(r"let bindAsync \(newPath: string\) \(oldPath: string\) \(flags: BindFlags\) \(session: SessionBox\) : Task<unit> =\s*task \{.*?updateSessionState session \(ProtocolSessionOps.withNamespace updatedNs\)\s*\}", "", text, flags=re.DOTALL)

# Fix OpenAsync, ReadAsync, WriteAsync, StatAsync, WstatAsync, ReaddirAsync
text = re.sub(r"runtime\.WriteAsync\(relativePath, t, dialect, ct\)", r"handler.WriteAsync(relativePath, t, ct)", text)
text = re.sub(r"runtime\.StatAsync\(relativePath, t, dialect, ct\)", r"handler.StatAsync(relativePath, t, ct)", text)
text = re.sub(r"runtime\.WstatAsync\(relativePath, t, dialect, ct\)", r"handler.WstatAsync(relativePath, t, ct)", text)
text = re.sub(r"runtime\.OpenAsync\(relativePath, t, dialect, ct\)", r"handler.OpenAsync(relativePath, t, ct)", text)
text = re.sub(r"runtime\.ReadAsync\(relativePath, t, dialect, ct\)", r"handler.ReadAsync(relativePath, t, ct)", text)

# ReaddirCompatAsync
text = re.sub(r"runtime\.ReaddirCompatAsync\(relativePath, t, dialect, ct\)", r"""let tsk = handler.ReaddirAsync(relativePath, t, ct)
                                                    if obj.ReferenceEquals(tsk, null) then
                                                        return raise (NinePProtocolException("Readdir not natively supported"))
                                                    let! response = tsk""", text)

with open("NinePSharp.Server.FSharp/Dispatcher_Rewrite2.fs", "w") as f:
    f.write(text)
