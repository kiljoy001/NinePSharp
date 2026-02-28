namespace NinePSharp.Core.FSharp

open System
open System.Security.Cryptography.X509Certificates
open NinePSharp.Constants
open NinePSharp.Messages
open NinePSharp.Parser

type TransportSession =
    { Protocol: ProtocolSession
      MSize: uint32 }

type ParseOutcome =
    { Session: TransportSession
      Message: NinePMessage
      ErrorResponse: obj }

type ResponseOutcome =
    { Session: TransportSession
      Response: obj }

module TransportSessionOps =
    let create (sessionId: string) (dialect: NinePDialect) (certificate: X509Certificate2) =
        { Protocol = ProtocolSessionOps.create sessionId dialect certificate
          MSize = 8192u }

    let withTransport (dialect: NinePDialect) (certificate: X509Certificate2) (session: TransportSession) =
        { session with Protocol = ProtocolSessionOps.withTransport dialect certificate session.Protocol }

    let certificateOrNull (session: TransportSession) =
        ProtocolSessionOps.certificateOrNull session.Protocol

    let parseMessage (buffer: ReadOnlyMemory<byte>) (tag: uint16) (session: TransportSession) =
        match NinePParser.parse session.Protocol.Dialect buffer with
        | Error err ->
            { Session = session
              Message = Unchecked.defaultof<NinePMessage>
              ErrorResponse = Rerror(tag, err) :> obj }
        | Ok msg ->
            { Session = session
              Message = msg
              ErrorResponse = null }

    let applyResponse (response: obj) (session: TransportSession) =
        match response with
        | :? Rversion as version ->
            { Session =
                { session with
                    MSize = version.MSize
                    Protocol =
                        ProtocolSessionOps.withTransport
                            (Dialect.fromString version.Version)
                            (certificateOrNull session)
                            session.Protocol }
              Response = response }
        | _ ->
            { Session = session
              Response = response }
