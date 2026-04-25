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
```

This runs the main test projects, repo-wide .NET analyzers, and the custom Semgrep rules in `quality/semgrep.yml`.

## NuGet Packages

Published packages are split by responsibility:

- `NinePSharp`
- `NinePSharp.Server.Abstractions`
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
