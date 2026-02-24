# Architecture

## Hybrid Engine

NinePSharp leverages the strengths of both C# and F#:

- **F# (Parser):** The protocol parser is built using F#'s powerful pattern matching and discriminated unions, ensuring that every 9P message is verified and valid before it reaches the business logic.
- **C# (Server & Backends):** The core server infrastructure and backends are built in C# to leverage the vast .NET ecosystem and high-performance task-based concurrency.

## Distributed Fabric (Akka.NET)

Using **Akka.Cluster**, NinePSharp nodes can discover each other automatically. This enables:

- **Remote Mounts:** Mount a filesystem from Node A onto Node B seamlessly.
- **Resilience:** The cluster handles node failures gracefully, ensuring the 9P namespace remains available.

## Zero-Exposure Security (LuxVault)

Security is not an afterthought. Every backend that handles sensitive data uses the **LuxVault** service:

- **Pinned Memory:** Secrets are stored in memory that the Garbage Collector cannot move or see.
- **Scoped Reveal:** Secrets are only "visible" for the microsecond they are needed for a cryptographic operation, then immediately zeroed.
