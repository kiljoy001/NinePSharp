namespace NinePSharp.Core.FSharp

open System.Security
open System.Security.Cryptography.X509Certificates
open NinePSharp.Constants
open NinePSharp.Server.Interfaces

type ProtocolSession =
    { SessionId: string
      Process: Plan9Process
      Fids: Map<uint32, Channel>
      AuthFids: Map<uint32, SecureString>
      Dialect: NinePDialect
      Certificate: X509Certificate2 option }

module ProtocolSessionOps =
    let create (sessionId: string) (dialect: NinePDialect) (certificate: X509Certificate2) =
        { SessionId = sessionId
          Process = Process.create 0 NamespaceOps.empty
          Fids = Map.empty
          AuthFids = Map.empty
          Dialect = dialect
          Certificate = Option.ofObj certificate }

    let withNamespace (ns: Namespace) (session: ProtocolSession) =
        { session with Process = { session.Process with Namespace = ns } }

    let withTransport (dialect: NinePDialect) (certificate: X509Certificate2) (session: ProtocolSession) =
        { session with
            Dialect = dialect
            Certificate = Option.ofObj certificate }

    let certificateOrNull (session: ProtocolSession) =
        session.Certificate |> Option.toObj

    let addAuthFid (fid: uint32) (secure: SecureString) (session: ProtocolSession) =
        { session with AuthFids = session.AuthFids |> Map.add fid secure }

    let tryFindAuthFid (fid: uint32) (session: ProtocolSession) =
        session.AuthFids |> Map.tryFind fid

    let removeAuthFid (fid: uint32) (session: ProtocolSession) =
        { session with AuthFids = session.AuthFids |> Map.remove fid }

    let containsFid (fid: uint32) (session: ProtocolSession) =
        session.Fids |> Map.containsKey fid

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
        let pathState = ChannelOps.createPathState visiblePath
        { Qid = { Type = qidType; Version = qidVersion; Path = qidPath }
          Offset = 0UL
          Target = target
          PathState = pathState
          IsOpened = false }

    let createBindingWithPathState
        (target: ChannelTarget)
        (qidType: QidType)
        (qidVersion: uint32)
        (qidPath: uint64)
        (pathState: PathState) =
        { Qid = { Type = qidType; Version = qidVersion; Path = qidPath }
          Offset = 0UL
          Target = target
          PathState = pathState
          IsOpened = false }

    let namespaceOf (session: ProtocolSession) =
        session.Process.Namespace

    let walkChannel
        (segments: seq<string>)
        (qidType: QidType)
        (qidVersion: uint32)
        (qidPath: uint64)
        (channel: Channel) =
        let walked = channel |> ChannelOps.walk (segments |> List.ofSeq)
        { walked with
            Qid = { Type = qidType; Version = qidVersion; Path = qidPath } }
