# Repository Guidelines

## Project Structure & Module Organization
`NinePSharp/` contains core 9P message types and constants (C#). `NinePSharp.Messages.FSharp/` and `NinePSharp.Parser/` provide F# protocol helpers and parsing. `NinePSharp.Server/` is the embeddable runtime library with backend adapters and configuration binding. `NinePSharp.Examples/` is the runnable sample host.  
Tests are split into `NinePSharp.Tests/` (C# unit/property/security tests) and `NinePSharp.Parser.Tests/` (F# parser tests). `NinePSharp.Fuzzer/` holds SharpFuzz entry points. Treat `publish_out/`, `TestResults/`, and log files as generated artifacts, not source.

## Build, Test, and Development Commands
Prefer the solution for full validation, or project-targeted commands when iterating:

- `dotnet build NinePSharp.sln -c Debug` builds the repo.
- `dotnet run --project NinePSharp.Examples/NinePSharp.Examples.csproj` starts the sample host locally.
- `dotnet test NinePSharp.Tests/NinePSharp.Tests.csproj` runs C# unit/property/security tests.
- `dotnet test NinePSharp.Parser.Tests/NinePSharp.Parser.Tests.fsproj` runs F# parser tests.
- `bash scripts/run-quality.sh` runs the standard analyzer/test/coverage/CRAP quality gate.
- `bash scripts/run-quality.sh --full` adds Stryker mutation testing and bounded SharpFuzz/AFL++ parser and filesystem campaigns.
- `bash test_integration.sh` runs the integration script (expects `/usr/local/bin/9p`).

## Coding Style & Naming Conventions
Use 4-space indentation in C# and idiomatic F# formatting in `.fs` modules. Keep nullable reference types enabled (`<Nullable>enable</Nullable>`).  
Use `PascalCase` for types/files and descriptive method names. Protocol message files follow Plan 9 direction prefixes (`T*`/`R*`, for example `Topen.cs`, `Ropen.fs`). Test classes end with `Tests`; property tests use `[Property]`.

StyleCop is wired for C# projects through `StyleCop.ruleset`; the initial hard gate enforces `SA1028` trailing-whitespace cleanliness. Follow existing local style and run `dotnet format` before large PRs.

## Testing Guidelines
Primary framework is xUnit, with FsCheck for property-based tests and Coverlet for merged coverage output.
Add or update tests with every protocol/backend/security fix. Prefer focused regression tests near the changed module (for example backend tests in `NinePSharp.Tests/*BackendTests.cs`).
Use `tools/crap.py --raw` only when auditing compiler-generated/F# closure noise; the default CRAP gate filters those known false positives and reports the exclusion count.

## Commit & Pull Request Guidelines
Recent commits use imperative, scope-first subjects (for example "Implement and verify SOAP backend", "Align core messages and dispatcher..."). Keep commits focused and behavior-oriented.
PRs should include: change summary, affected modules, exact test commands run, and any config/runtime prerequisites. Link issues when available.

## Security & Configuration Tips
Server settings live in `NinePSharp.Server/config.json`; keep secrets out of committed config. TLS endpoints now depend on explicit certificate settings in config rather than embedded secret-management helpers.
