#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
wasm_source="$(realpath -- "${1:?usage: check-wasm-compatibility.sh /path/to/dotnet-webassembly}")"
expected_revision=70c46c0ee78abbdca92490a1881fe509605c5c3d
actual_revision="$(git -C "$wasm_source" rev-parse HEAD)"
if [[ "$actual_revision" != "$expected_revision" ]] || [[ -n "$(git -C "$wasm_source" status --porcelain --untracked-files=normal)" ]]; then
    echo "Expected a clean dotnet-webassembly checkout at $expected_revision" >&2
    exit 1
fi

dotnet build "$wasm_source/WebAssembly/WebAssembly.csproj" -c Release -f net9.0 --nologo
dotnet test "$repo_root/tools/WasmCompatibility.Tests/WasmCompatibility.Tests.csproj" \
    -c Release "-p:WasmSource=$wasm_source" --nologo \
    --logger 'trx;LogFileName=wasm-compatibility.trx' \
    --results-directory "$repo_root/.artifacts/wasm-compatibility"
