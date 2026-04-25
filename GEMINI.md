# GEMINI.md - NinePSharp Project Context

## Project Overview

NinePSharp is a .NET 9P toolkit implemented across C# and F#. The current codebase is intentionally lean: core wire types, parser, transport/session handling, and a small server runtime with filesystem-style request handlers.

### Core Features
- **Protocol Support:** 9P2000.u and selected 9P2000.L message handling.
- **Hybrid Implementation:** C# for runtime/server code, F# for parser and transport-session helpers.
- **Minimal Session Model:** Per-session FID tracking and backend-path dispatch without the older namespace/mount kernel.

## Project Structure

- **NinePSharp/**: Core C# library containing 9P message definitions and constants.
- **NinePSharp.Parser/**: F# parser implementation.
- **NinePSharp.Server/**: Embeddable runtime library and transport processing.
- **NinePSharp.Examples/**: Runnable sample host.
- **NinePSharp.Tests/**: Unit and regression tests.

## Technology Stack

- **Runtime:** .NET 10.0.
- **Languages:** C# and F#.
- **Testing:** xUnit, FsCheck, Coyote, Coverlet.

## Commands

- **Build:** `dotnet build NinePSharp.Server/NinePSharp.Server.csproj`
- **Run example:** `dotnet run --project NinePSharp.Examples/NinePSharp.Examples.csproj`
- **Run tests:** `bash scripts/run-quality.sh`

## Development Conventions

- Keep the runtime surface small and explicit.
- Prefer primary protocol/documentation sources over inferred behavior.
- Add focused regression tests for transport, parser, and backend behavior when fixing bugs.
