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

## BIP-39 English Wordlist

`NinePSharp.Fog.Auth/Resources/bip39-english.txt` is the BIP-39 English wordlist, copied unchanged from `bip-0039/english.txt` in the Bitcoin Improvement Proposals repository (SHA-256 `2f5eed53a4727b4bf8880d8f3f199efc90e58503646d9ff8eff3a2ed3b24dbda`). BIP 39 is licensed under the MIT License. The keyfs recovery phrase encodes its storage key with it, and its tests use the reference vectors of trezor/python-mnemonic (MIT).

Reference upstream:
- https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki
- https://github.com/trezor/python-mnemonic
