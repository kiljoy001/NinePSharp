# NinePSharp

NinePSharp is a modular .NET 9P toolkit and server. It lets you expose local or service-backed resources through a unified 9P filesystem interface.

## Why NinePSharp

- Core 9P protocol primitives and message types in reusable packages.
- Batteries-included server runtime with dependency injection and backend routing.
- Plugin-style backends so deployments only load what they need.
- Runnable example host for embedded or local server scenarios.

## Repository Layout

- `NinePSharp/` - core protocol constants, message types, serialization helpers.
- `NinePSharp.Parser/` - F# parser implementation.
- `NinePSharp.Server.Abstractions/` - shared interfaces/contracts for backends.
- `NinePSharp.Server/` - runtime library and transport host components.
- `NinePSharp.Namespaces/` - Plan 9 mount tables, virtual process groups, fid sessions, and resource contracts.
- `NinePSharp.Namespaces.Orleans/` - distributed namespace and data-plane adapters for Orleans grains.
- `NinePSharp.Namespaces.Orleans.Server/` - 9P2000 and maintained 9P2000.L-subset dispatcher for Orleans resources.
- `NinePSharp.Examples/` - runnable sample host with an in-memory handler.
- `NinePSharp.Tests/` and `NinePSharp.Parser.Tests/` - test suites.

Fog, the Plan 9-style application platform built on these libraries, lives in its own
repository, `kiljoy001/Fog`, and takes NinePSharp from NuGet.

## Quick Start

Prerequisites:
- .NET SDK 10.x

Build the library and run the example host:

```bash
dotnet build NinePSharp.Server/NinePSharp.Server.csproj -c Release
dotnet run --project NinePSharp.Examples/NinePSharp.Examples.csproj
```

`NinePSharp.Server` is the embeddable runtime library. `NinePSharp.Examples` is the current runnable sample host.

## Tests

```bash
dotnet test NinePSharp.Tests/NinePSharp.Tests.csproj -c Release
dotnet test NinePSharp.Parser.Tests/NinePSharp.Parser.Tests.fsproj -c Release
```

Integration script (requires a local `9p` CLI):

```bash
bash test_integration.sh
```

## Quality

```bash
bash scripts/run-quality.sh
bash scripts/run-quality.sh --full
FUZZ_SECONDS=30 bash scripts/fuzz.sh parser
FUZZ_SECONDS=30 bash scripts/fuzz.sh namespace-syscalls
```

The standard gate runs a Release build, StyleCop/.NET analyzers, all main BDD,
xUnit, and FsCheck suites, merged Coverlet coverage, stale-coverage checks, coverage
thresholds, CRAP scoring, and the custom Semgrep rules in `quality/semgrep.yml` when
Semgrep is installed.

`--full` also runs the scoped Stryker mutation gate (`MIN_MUTATION=100` by default)
and bounded SharpFuzz/AFL++ parser, filesystem, namespace, namespace-syscall, authorization and
Orleans gateway campaigns. Mutation scopes include the Orleans adapters, gateway, shared transport,
and sequential control client. Coverage
defaults can be tuned with `MIN_LINE` and `MIN_BRANCH`; CRAP failure is controlled by
`CRAP_LIMIT`.

## Orleans gateway

Run a read-only, loopback example with `dotnet run --project NinePSharp.Examples -- --orleans 5640`.
Attach as `guest` and read `/hello`. See [hosting, provider registration, security,
and lifetime semantics](docs/orleans-integration.md) for embedding the gateway in your own host.

## Plan 9 namespaces

Current development focuses on Plan 9 namespace behavior: virtual processes and
process groups, bind/mount/unmount, union mounts, channel and fid lifetime, and
control files. See the [namespace semantics](docs/plan9-namespace-semantics.md) and
[BDD specifications](docs/specifications/plan9-namespace/README.md).
Current [namespace validation evidence](docs/namespace-verification.md) records
the tested scope and its limitations.

The local namespace feature set includes independent descriptor groups and
explicit resource cleanup: final-owner exit, close-on-exec, empty-table rfork,
admitted I/O references, and provider-close failure handling. These have
[executable cleanup scenarios](NinePSharp.Namespaces.Tests/Features/DescriptorCleanup.feature).
Durable Orleans ownership and cleanup recovery remain separate implementation work.

Pinned [dotnet-webassembly compatibility tests](tools/WasmCompatibility.Tests/README.md) run in
CI, checking WASI file access through the virtual namespace.

## NuGet Packages

Package artifacts are split by responsibility:

- `NinePSharp`
- `NinePSharp.Parser`
- `NinePSharp.Client`
- `NinePSharp.Server.Abstractions`
- `NinePSharp.Server` (with `NinePSharp.Core.FSharp` and `NinePSharp.Server.FSharp`)
- `NinePSharp.Namespaces`
- `NinePSharp.Namespaces.Orleans.Abstractions`
- `NinePSharp.Namespaces.Orleans`
- `NinePSharp.Namespaces.Orleans.Server`
- `NinePSharp.Backends.*` (one package per backend family)

Install example:

```bash
dotnet add package NinePSharp
dotnet add package NinePSharp.Server.Abstractions
dotnet add package NinePSharp.Backends.Database
```

Each package folder includes its own `README.NUGET.md`.

## Configuration & Security

- Main runtime config: `NinePSharp.Server/config.json`

## License

Licensed under MIT (`LICENSE`).  
See `THIRD_PARTY_NOTICES.md` for upstream notices and attribution details.
