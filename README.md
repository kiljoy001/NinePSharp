# NinePSharp

NinePSharp is a modular .NET 9P toolkit and server. It lets you expose heterogeneous systems (databases, cloud services, RPC endpoints, and blockchain providers) through a unified 9P filesystem interface.

## Why NinePSharp

- Core 9P protocol primitives and message types in reusable packages.
- Batteries-included server runtime with dependency injection and backend routing.
- Plugin-style backends so deployments only load what they need.
- Security-focused secret handling via transient session keys and encrypted vault files.
- Cluster-ready host components for multi-node deployments.

## Repository Layout

- `NinePSharp/` - core protocol constants, message types, serialization helpers.
- `NinePSharp.Parser/` - F# parser implementation.
- `NinePSharp.Server.Abstractions/` - shared interfaces/contracts for backends.
- `NinePSharp.Backends.*` - optional backend plugins:
  - `Database`, `Cloud`, `JsonRpc`, `Websocket`, `Mqtt`, `Rest`, `Soap`, `Grpc`, `Blockchain`
- `NinePSharp.Server/` - runtime host/server (not shipped as a NuGet package).
- `NinePSharp.Tests/` and `NinePSharp.Parser.Tests/` - test suites.

## Quick Start

Prerequisites:
- .NET SDK 10.x

Build and run the server:

```bash
dotnet build NinePSharp.Server/NinePSharp.Server.csproj -c Release
dotnet run --project NinePSharp.Server/NinePSharp.Server.csproj
```

Default server endpoint is configured in `NinePSharp.Server/config.json` (`127.0.0.1:5641`).

## Tests

```bash
dotnet test NinePSharp.Tests/NinePSharp.Tests.csproj -c Release
dotnet test NinePSharp.Parser.Tests/NinePSharp.Parser.Tests.fsproj -c Release
```

Integration script (requires a local `9p` CLI):

```bash
bash test_integration.sh
```

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
- Cluster config: `NinePSharp.Server/cluster.conf`
- Vault/secret handling is designed for minimized plaintext exposure and startup/shutdown cleanup.

## License

Licensed under MIT (`LICENSE`).  
See `THIRD_PARTY_NOTICES.md` for upstream notices and attribution details.
