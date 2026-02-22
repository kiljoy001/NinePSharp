# Third-Party Notices

This repository includes or interoperates with third-party software and ideas.

## 9front / Plan 9 Derived Material

Some logic and structures in this project are derived from or informed by Plan 9 / 9front source and documentation.

Important:
- Any directly copied or adapted upstream code retains its original upstream license terms.
- The repository-level MIT license applies to original NinePSharp code authored in this repository.
- Upstream notices and license obligations must be preserved for derived files.

Reference upstream:
- https://git.9front.org/plan9front/plan9front/
- https://plan9.io/plan9/

## diod (9P2000.L Related Material)

This project implements 9P2000.L behavior and may be conceptually informed by the diod project.

Important:
- The upstream diod distribution includes GPLv2 licensing material.
- A provenance audit was run on February 22, 2026 against `diod-1.0.24` and found no evidence of direct code copy into current `NinePSharp`, `NinePSharp.Parser`, and `NinePSharp.Server` C#/F# sources.
- If any future code is copied or adapted from diod, that code must keep upstream notices and GPLv2 obligations in the affected files/package.
- Reproducible check: `scripts/audit_diod_provenance.sh`

Reference upstream:
- https://github.com/chaos/diod
- https://sources.debian.org/src/diod/

## Monocypher

This repository vendors Monocypher sources under `NinePSharp.Server/external/monocypher/`.

Monocypher is dual-licensed by its author. See headers in:
- `NinePSharp.Server/external/monocypher/monocypher.c`
- `NinePSharp.Server/external/monocypher/monocypher.h`
