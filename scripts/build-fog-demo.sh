#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet restore NinePSharp.Fog.Math/NinePSharp.Fog.Math.csproj -r linux-x64
dotnet publish NinePSharp.Fog.Math/NinePSharp.Fog.Math.csproj -c Release -r linux-x64 \
  --self-contained true -p:RestoreLockedMode=true -o .artifacts/fog-demo/math
dotnet publish NinePSharp.Fog.Demo/NinePSharp.Fog.Demo.csproj -c Release -r linux-x64 \
  --self-contained true -p:RuntimeFrameworkVersion=10.0.9 -o .artifacts/fog-demo/host
python3 - <<'PY'
import hashlib, json
from pathlib import Path
root = Path('.artifacts/fog-demo')
manifest = {
    'provider': 'angouri-2.4.0-demo1', 'framework': 'Microsoft.NETCore.App/10.0.9',
    'target': 'linux-x64',
    'files': {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
              for name in ('host', 'math') for p in sorted((root / name).rglob('*')) if p.is_file()},
}
(root / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
PY
