namespace NinePSharp.Core.FSharp

open System.Security.Cryptography.X509Certificates
open NinePSharp.Constants

type ProtocolSession =
    { SessionId: string
      Fids: Map<uint32, Channel>
      Dialect: NinePDialect
      Certificate: X509Certificate2 option }

module ProtocolSessionOps =
    let create (sessionId: string) (dialect: NinePDialect) (certificate: X509Certificate2) =
        { SessionId = sessionId
          Fids = Map.empty
          Dialect = dialect
          Certificate = Option.ofObj certificate }

    let withTransport (dialect: NinePDialect) (certificate: X509Certificate2) (session: ProtocolSession) =
        { session with
            Dialect = dialect
            Certificate = Option.ofObj certificate }

    let certificateOrNull (session: ProtocolSession) =
        session.Certificate |> Option.toObj

    let tryFindFid (fid: uint32) (session: ProtocolSession) =
        session.Fids |> Map.tryFind fid

    let bindFid (fid: uint32) (channel: Channel) (session: ProtocolSession) =
        { session with Fids = session.Fids |> Map.add fid channel }

    let removeFid (fid: uint32) (session: ProtocolSession) =
        { session with Fids = session.Fids |> Map.remove fid }

    let createBinding
        (target: ChannelTarget)
        (qidType: QidType)
        (qidVersion: uint32)
        (qidPath: uint64)
        (visiblePath: seq<string>) =
        { Qid = { Type = qidType; Version = qidVersion; Path = qidPath }
          Offset = 0UL
          Target = target
          InternalPath = visiblePath |> List.ofSeq
          IsOpened = false
        }
