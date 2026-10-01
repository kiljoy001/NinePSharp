# dotnet-webassembly compatibility checks

This opt-in xUnit integration suite runs compiled WASM against the existing namespace
data plane and descriptor leases. CI checks out upstream at
`70c46c0ee78abbdca92490a1881fe509605c5c3d` and runs it before the normal full quality gate.
No floating NuGet dependency or WASM engine reference is added to namespace packages.

```sh
git clone https://github.com/RyanLamansky/dotnet-webassembly /tmp/fog-dotnet-webassembly
git -C /tmp/fog-dotnet-webassembly checkout 70c46c0ee78abbdca92490a1881fe509605c5c3d
bash scripts/check-wasm-compatibility.sh /tmp/fog-dotnet-webassembly
```

Requires .NET 10 SDK and the net9.0 reference pack for upstream. The script verifies
the source revision and clean tree, builds upstream, then runs ten test cases. TRX
evidence is in `.artifacts/wasm-compatibility`. The default solution gate stays offline
with respect to this optional upstream checkout; CI runs both gates explicitly.

Evidence: Task/ValueTask imports are rejected; real numeric WASI imports perform file
write/positioned read/close, short reads and EOF; memory growth uses the new page;
invalid memory ranges cause no writes; a pending async write retains its channel
while a concurrent close detaches the descriptor. A dedicated test worker blocks,
while the coordinating test continues. This does not test an Orleans silo.

`FileHost` is a deliberately narrow fixture: one host-opened regular file, one vector
per call, one write at offset zero, and only the errors needed by those cases. It is
not a production WASI implementation or an untrusted-code runner. It supplies no
path_open, rights/preopens, general vector IO, shared offsets, metering or containment.
Production requirements and pending BDD are in
[Wasm.md](../../docs/specifications/fog-v1-profiles/Wasm.md). These integration tests
do not replace the normal property, fuzz or zero-survivor mutation gates for production
adapter code when it is added.
