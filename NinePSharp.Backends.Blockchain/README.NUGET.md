# NinePSharp.Backends.Blockchain

Blockchain backend plugin for accessing chain data and operations through 9P.

## Included integrations
- Bitcoin
- Ethereum
- Solana
- Cardano
- Stellar

## Install
```bash
dotnet add package NinePSharp.Backends.Blockchain
```

Use for filesystem-style interaction patterns over supported blockchain networks.

## Solana wallet import
`/solana/wallets/import` accepts:
- `password:base58PrivateKey`
- `password:base58PrivateKey:base58PublicKey`

If only the private key is provided, the backend attempts to derive and persist the matching public key for stable unlock/address behavior.

## Cardano live mode
Cardano can run in mock mode by default, or in live read mode through Blockfrost.

Configuration keys:
- `Network`: `Mainnet`, `Preprod`, `Preview`, or `Testnet`
- `BlockfrostProjectId`: required for live mode
- `BlockfrostApiUrl`: optional override (defaults from `Network`)

When live mode is enabled, `/cardano/send` accepts:
- `address:amount` (auto-build/sign/submit using unlocked mnemonic)
- `cbor:<hex>` (or raw even-length hex) for pre-signed transactions
