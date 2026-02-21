# GEMINI.md - NinePSharp Project Context

## Project Overview

NinePSharp is a high-performance, extensible 9P protocol server implemented in .NET (C# and F#). It provides a unified file-system interface (9P2000.u and 9P2000.L) to various backend services, with a strong focus on blockchain integration. By mounting different blockchains as directories, it allows standard file-system tools to interact with decentralized networks.

### Core Features:
- **Multi-Backend Support:** Interfaces for Ethereum, Bitcoin, Cardano, Solana, Stellar, and generic Database/JSON-RPC backends.
- **Protocol Support:** Implements 9P2000.u (Unix extensions) and elements of 9P2000.L (Linux extensions).
- **LuxVault:** A secure secret management system using Monocypher-based XChaCha20-Poly1305 encryption.
- **Hybrid Implementation:** Leverages C# for server infrastructure and F# for robust protocol parsing.

## Security Architecture (Zero-Exposure)

- **Transient Session Key:** Rooted in a 64-bit random seed generated at boot and stored in a `SecureString`. The session key is never saved to disk.
- **Pinned Memory:** All sensitive buffers (keys, cleartext) are allocated using pinned arrays (`GC.AllocateArray(..., pinned: true)`) to prevent GC fragments.
- **Scoped Reveal Pattern:** Secrets are wrapped in `ProtectedSecret` and only momentarily "revealed" via the `Use(ReadOnlySpan<byte>)` method, with mandatory zeroing immediately after.
- **Secure Pipeline:** `SecureString` is used for all sensitive inputs entering the server via 9P `Tauth` or wallet operations.

## Project Structure

- **NinePSharp/**: Core C# library containing 9P message definitions and constants.
- **NinePSharp.Parser/**: F# library providing the core 9P protocol parser.
- **NinePSharp.Server/**: Main executable. Hosts the TCP server and dispatches requests.
- **NinePSharp.Tests/**: Suite of unit, integration, and security regression tests.

## Technology Stack

- **Runtimes:** .NET 9.0 / .NET 10.0.
- **Languages:** C# and F#.
- **Cryptography:** Monocypher (native library via corrected P/Invoke).

## Building and Running

### Prerequisites:
- .NET 9.0 and .NET 10.0 SDKs.
- `libmonocypher.so` in the server's output directory.

### Commands:
- **Build All:** `dotnet build`
- **Run Server:** `dotnet run --project NinePSharp.Server`
- **Run Tests:** `dotnet test`

## Development Conventions

- **Security First:** Never use raw strings for secrets. Use `ProtectedSecret` and the `Use` pattern.
- **Explicit Zeroing:** Always use `try...finally` with `Array.Clear` for any buffer that touched a secret.
- **Protocol Purity:** Strictly follow the 9P2000 specification.
