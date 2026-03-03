namespace NinePSharp.Core.FSharp

open System
open System.Buffers.Binary
open System.IO.Hashing
open System.Text
open Microsoft.FSharp.NativeInterop

#nowarn "9"

/// Defines the 10-bit System Policy Mask for Fast-Path enforcement.
/// These bits are embedded in the UUID v8 for O(1) policy checks.
[<Flags>]
type PathPolicy =
    | None        = 0x000us
    | TierMask    = 0x003us // Bits 0-1: Storage Tier (RAM, Disk, Consensus, Cold)
    | LevelMask   = 0x00Cus // Bits 2-3: Sensitivity (Public, Internal, Restricted, Critical)
    | Immutable   = 0x010us // Bit 4: WORM (Write Once Read Many)
    | SessionOnly = 0x020us // Bit 5: Session-Local isolation
    | Verified    = 0x040us // Bit 6: Formally Verified / Signed
    | Ephemeral   = 0x080us // Bit 7: Auto-delete on clunk
    | AuditTrace  = 0x100us // Bit 8: Verbose operation logging
    | ProxyGate   = 0x200us // Bit 9: Gateway to remote server

/// Provides professional identity services using UUID v8 and XxHash64.
/// Complies with RFC 9562 and 9P2000 requirements.
module PathHash =

    /// Generates a stable 64-bit Qid.path using XxHash64.
    /// This provides a collision-resistant 64-bit value required for the 9P wire format.
    let stableHash (kind: char) (path: string list) : uint64 =
        let splitAndNormalize (pathStr: string) =
            pathStr.Split([| '/' |], StringSplitOptions.RemoveEmptyEntries)
            |> List.ofArray

        let normalized =
            match splitAndNormalize ("/" + String.Join("/", path)) with
            | [] -> "/"
            | segments -> "/" + String.Join("/", segments)

        let key = String.Concat(kind, ":", normalized)
        let bytes = Encoding.UTF8.GetBytes(key)
        
        // Use standard 64-bit XxHash for professional collision resistance
        XxHash64.HashToUInt64(ReadOnlySpan<byte>(bytes))

    /// Constructs an RFC 9562 compliant UUID v8 embedding a 64-bit identity + context.
    /// Layout: [48-bit Context A][4-bit Ver][10-bit Policy Mask][2-bit Var][64-bit Qid.Path]
    let createInternalId (contextA: uint64) (policy: PathPolicy) (qidPath: uint64) : Guid =
        let mutable bytesArr = Array.zeroCreate<byte> 16
        let bytes = Span<byte>(bytesArr)
        
        // 1. Pack Context A (Top 48 bits)
        BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(0, 4), uint32 (contextA >>> 16))
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(4, 2), uint16 (contextA &&& 0xFFFFUL))
        
        // 2. Pack the 64-bit Qid.path into the bottom 8 bytes (64 bits)
        BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(8, 8), qidPath)
        
        // 3. Set Version 8 (bits 48-51) and Variant 1 (bits 64-65) per RFC 9562
        bytes.[6] <- (bytes.[6] &&& 0x0Fuy) ||| 0x80uy // Version 8
        bytes.[8] <- (bytes.[8] &&& 0x3Fuy) ||| 0x80uy // Variant 1
        
        // 4. Inject 10-bit Policy Mask (Context B) into bits 52-61
        let policyBits = (uint16 policy &&& 0x03FFus) <<< 2
        bytes.[7] <- byte (policyBits >>> 4)
        let b6 = uint16 bytes.[6]
        bytes.[6] <- byte (b6 ||| (policyBits &&& 0x000Fus))

        Guid(ReadOnlySpan<byte>(bytesArr))

    /// Extracts the raw 64-bit Qid.path for the 9P wire.
    let toWirePath (id: Guid) : uint64 =
        let mutable bytesArr = Array.zeroCreate<byte> 16
        let bytes = Span<byte>(bytesArr)
        if not (id.TryWriteBytes(bytes)) then
            raise (InvalidOperationException("Guid extraction failed"))
        
        BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(8, 8))

    /// Extracts the 10-bit Policy Mask from a UUID v8.
    let extractPolicy (id: Guid) : PathPolicy =
        let mutable bytesArr = Array.zeroCreate<byte> 16
        let bytes = Span<byte>(bytesArr)
        id.TryWriteBytes(bytes) |> ignore
        let b6 = uint16 (bytes.[6] &&& 0x0Fuy)
        let b7 = uint16 (bytes.[7])
        let policyBits = (b7 <<< 4 ||| b6) >>> 2
        LanguagePrimitives.EnumOfValue<uint16, PathPolicy>(policyBits)
