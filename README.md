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
- `NinePSharp.Fog/` - bounded LibTab records and ephemeral control transactions.
- `NinePSharp.Fog.Server/` - direct TLS node listener, 9P control files, and transaction client.
- `NinePSharp.Examples/` - runnable sample host with an in-memory handler.
- `NinePSharp.Tests/` and `NinePSharp.Parser.Tests/` - test suites.

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

`--full` also runs the scoped Stryker mutation gate (`MIN_MUTATION=90` by default)
and bounded SharpFuzz/AFL++ parser, filesystem, namespace, namespace-syscall, Orleans gateway, and Fog campaigns.
Mutation scopes include the Orleans adapters, gateway, shared transport, Fog core/server,
and sequential control client. Coverage
defaults can be tuned with `MIN_LINE` and `MIN_BRANCH`; CRAP failure is controlled by
`CRAP_LIMIT`.

## Orleans gateway

Run a read-only, loopback example with `dotnet run --project NinePSharp.Examples -- --orleans 5640`.
Attach as `guest` and read `/hello`. See [hosting, provider registration, security,
and lifetime semantics](docs/orleans-integration.md) for embedding the gateway in your own host.

## Exploratory fog

The [Fog control adapter](NinePSharp.Fog.Server/README.md) exposes bounded transactions
as ordinary 9P files. The broader [fog contracts](docs/specifications/fog-v1-profiles/README.md)
propose AngouriMath jobs and WASM programs behind the same workload interface;
an [experimental direct math worker](docs/fog-math-demo.md) now runs AngouriMath jobs
on another Linux machine and retains results across client reconnects. The full
provider/scheduler protocols and WASM runner remain to be implemented.
[Experiments](docs/fog-experiments.md) explore mathematical workbenches, parameter
sweeps, procedural generation and visible failure/recovery behavior.

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
