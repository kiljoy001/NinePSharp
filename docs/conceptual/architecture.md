# Architecture

## Hybrid Engine

NinePSharp leverages the strengths of both C# and F#:

- **F# (Parser):** The protocol parser is built using F#'s powerful pattern matching and discriminated unions, ensuring that every 9P message is verified and valid before it reaches the business logic.
- **C# (Server & Backends):** The core server infrastructure and backends are built in C# to leverage the vast .NET ecosystem and high-performance task-based concurrency.

## Runtime Model

The current runtime keeps the server model deliberately small:

- **Transport:** TCP or TLS framing and session lifecycle.
- **Parser:** Strongly typed C#/F# message parsing and validation.
- **Dispatcher:** Per-session FID tracking with direct backend-path dispatch.
- **Backends:** Request handlers that implement filesystem behavior for a mounted root.
