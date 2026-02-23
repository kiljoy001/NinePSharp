using System;
using System.Security;
using System.Threading.Tasks;

namespace NinePSharp.Server.Interfaces;

/// <summary>
/// A provider that handles blockchain signing and identity management.
/// Implementations can range from local LuxVault-based signing to hardware wallets (Ledger/Trezor) or remote KMS.
/// </summary>
public interface IBlockchainWalletProvider
{
    /// <summary>
    /// The unique name of the provider (e.g., "LuxVault", "EmerVault", "Ledger").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Unlocks an identity for signing operations.
    /// This might involve prompting for a password, fetching a KDF seed from Emercoin, or checking for a hardware wallet.
    /// </summary>
    /// <param name="identity">A unique identifier for the wallet (e.g., account name or public key).</param>
    /// <param name="credentials">Optional password or PIN required to unlock the identity.</param>
    /// <returns>True if the identity was successfully unlocked.</returns>
    Task<bool> UnlockAsync(string identity, SecureString? credentials);

    /// <summary>
    /// Signs a raw transaction or message hash.
    /// The provider never exposes the private key to the caller.
    /// </summary>
    /// <param name="identity">The identity to sign with.</param>
    /// <param name="dataToSign">The raw transaction data or hash to sign.</param>
    /// <param name="chainType">The blockchain type (e.g., "ethereum", "bitcoin", "solana").</param>
    /// <returns>The signed signature bytes.</returns>
    Task<byte[]> SignAsync(string identity, byte[] dataToSign, string chainType);

    /// <summary>
    /// Gets the public address for a given identity and blockchain.
    /// </summary>
    /// <param name="identity">The identity to lookup.</param>
    /// <param name="chainType">The blockchain type (e.g., "ethereum", "bitcoin").</param>
    /// <returns>The public address string.</returns>
    Task<string?> GetAddressAsync(string identity, string chainType);

    /// <summary>
    /// Returns true if the identity is currently unlocked and ready for signing.
    /// </summary>
    bool IsUnlocked(string identity);
}
