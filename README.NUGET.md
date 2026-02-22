# NinePSharp Packages

NinePSharp provides a modular 9P toolkit for .NET.

## Package Layout
- `NinePSharp`: core 9P protocol primitives and message types.
- `NinePSharp.Server.Abstractions`: backend contracts and shared server abstractions.
- `NinePSharp.Backends.*`: optional protocol/data backends (Database, Cloud, JsonRpc, Websocket, Mqtt, Rest, Soap, Grpc, Blockchain).

## Typical Usage
1. Reference `NinePSharp` for protocol/core usage.
2. Reference `NinePSharp.Server.Abstractions` if implementing custom backends.
3. Add only the backend plugin packages you actually deploy.

## Notes
- Packages are intentionally split to keep dependency footprints minimal.
- Runtime server wiring is provided by `NinePSharp.Server` in this repository.
