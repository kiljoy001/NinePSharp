# Repository Guidelines

## Project Structure & Module Organization
`NinePSharp/` contains core 9P message types and constants (C#). `NinePSharp.Messages.FSharp/` and `NinePSharp.Parser/` provide F# protocol helpers and parsing. `NinePSharp.Server/` is the executable host with backend adapters (`backends/`), cluster code (`cluster/`), and configuration binding (`configuration/`).  
Tests are split into `NinePSharp.Tests/` (C# unit/property/security tests) and `NinePSharp.Parser.Tests/` (F# parser tests). `NinePSharp.Fuzzer/` holds SharpFuzz entry points. Treat `publish_out/`, `*.vlt`, and log files as generated artifacts, not source.

## Build, Test, and Development Commands
The root `NinePSharp.slnx` currently has no project entries, so use project-targeted commands:

- `dotnet build NinePSharp.Server/NinePSharp.Server.csproj -c Debug` builds the server and copies runtime config.
- `dotnet run --project NinePSharp.Server/NinePSharp.Server.csproj` starts the 9P server locally.
- `dotnet test NinePSharp.Tests/NinePSharp.Tests.csproj` runs C# unit/property/security tests.
- `dotnet test NinePSharp.Parser.Tests/NinePSharp.Parser.Tests.fsproj` runs F# parser tests.
- `dotnet test NinePSharp.Tests/NinePSharp.Tests.csproj --collect:"XPlat Code Coverage"` collects coverage via Coverlet.
- `bash test_integration.sh` runs the integration script (expects `/usr/local/bin/9p`).

## Coding Style & Naming Conventions
Use 4-space indentation in C# and idiomatic F# formatting in `.fs` modules. Keep nullable reference types enabled (`<Nullable>enable</Nullable>`).  
Use `PascalCase` for types/files and descriptive method names. Protocol message files follow Plan 9 direction prefixes (`T*`/`R*`, for example `Topen.cs`, `Ropen.fs`). Test classes end with `Tests`; property tests use `[Property]`.

No repository-level linter/style config is checked in; follow existing local style and run `dotnet format` before large PRs.

## Testing Guidelines
Primary framework is xUnit, with FsCheck for property-based tests and `coverlet.collector` for coverage output.  
Add or update tests with every protocol/backend/security fix. Prefer focused regression tests near the changed module (for example backend tests in `NinePSharp.Tests/*BackendTests.cs`).

## Commit & Pull Request Guidelines
Recent commits use imperative, scope-first subjects (for example "Implement and verify SOAP backend", "Align core messages and dispatcher..."). Keep commits focused and behavior-oriented.  
PRs should include: change summary, affected modules, exact test commands run, and any config/runtime prerequisites. Link issues when available.

## Security & Configuration Tips
Server settings live in `NinePSharp.Server/config.json`; keep secrets out of committed config. Use the protected secret/vault patterns already in the server code for sensitive data handling, and avoid committing generated vault artifacts (`secret_*.vlt`).
