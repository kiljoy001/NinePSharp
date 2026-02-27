using System;
using System.Security;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Interfaces;

/// <summary>
/// Service for zero-exposure secret management and protocol-level encryption.
/// </summary>
public interface ILuxVaultService
{
    /// <summary>
    /// Gets the secure memory arena shards for protected buffer allocations.
    /// </summary>
    System.Collections.Generic.IEnumerable<SecureMemoryArena> Arenas { get; }

    /// <summary>
    /// Selects an arena shard based on the current thread ID.
    /// </summary>
    SecureMemoryArena GetLocalArena();

    /// <summary>
    /// Gets the physical path for a vault file.
    /// </summary>
    /// <param name="fileName">The name of the vault file.</param>
    /// <returns>The full filesystem path.</returns>
    string GetVaultPath(string fileName);

    /// <summary>
    /// Generates a deterministic hidden identifier from a seed.
    /// </summary>
    /// <param name="seed">A 32-byte seed.</param>
    /// <returns>A hex-encoded hidden identifier.</returns>
    string GenerateHiddenId(ReadOnlySpan<byte> seed);

    /// <summary>
    /// Derives a 32-byte seed from a password and nonce.
    /// </summary>
    /// <param name="password">The master password.</param>
    /// <param name="nonce">A unique nonce or salt.</param>
    /// <param name="output">The buffer to receive the derived seed.</param>
    void DeriveSeed(string password, ReadOnlySpan<byte> nonce, Span<byte> output);

    /// <summary>
    /// Derives a 32-byte seed from a SecureString password and nonce.
    /// </summary>
    /// <param name="password">The master password as a SecureString.</param>
    /// <param name="nonce">A unique nonce or salt.</param>
    /// <param name="output">The buffer to receive the derived seed.</param>
    void DeriveSeed(SecureString password, ReadOnlySpan<byte> nonce, Span<byte> output);

    /// <summary>
    /// Encrypts plaintext using a password.
    /// </summary>
    /// <param name="plaintext">The cleartext to encrypt.</param>
    /// <param name="password">The master password.</param>
    /// <returns>The encrypted payload containing salt, nonce, MAC, and ciphertext.</returns>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, string password);

    /// <summary>
    /// Encrypts plaintext using a SecureString password.
    /// </summary>
    /// <param name="plaintext">The cleartext to encrypt.</param>
    /// <param name="password">The master password.</param>
    /// <returns>The encrypted payload.</returns>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, SecureString password);

    /// <summary>
    /// Encrypts plaintext using raw key material.
    /// </summary>
    /// <param name="plaintext">The cleartext to encrypt.</param>
    /// <param name="keyMaterial">Raw entropy for key derivation.</param>
    /// <returns>The encrypted payload.</returns>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> keyMaterial);
    
    /// <summary>
    /// Decrypts a payload into a secure, pinned memory buffer.
    /// </summary>
    /// <param name="payload">The encrypted payload.</param>
    /// <param name="password">The master password.</param>
    /// <returns>A SecureSecret containing the cleartext, or null if validation fails.</returns>
    SecureSecret? DecryptToBytes(byte[] payload, string password);

    /// <summary>
    /// Decrypts a payload into a secure, pinned memory buffer using a SecureString password.
    /// </summary>
    /// <param name="payload">The encrypted payload.</param>
    /// <param name="password">The master password.</param>
    /// <returns>A SecureSecret containing the cleartext.</returns>
    SecureSecret? DecryptToBytes(byte[] payload, SecureString password);

    /// <summary>
    /// Decrypts a payload into a secure, pinned memory buffer using raw key material.
    /// </summary>
    /// <param name="payload">The encrypted payload.</param>
    /// <param name="keyMaterial">Raw entropy for key derivation.</param>
    /// <returns>A SecureSecret containing the cleartext.</returns>
    SecureSecret? DecryptToBytes(byte[] payload, ReadOnlySpan<byte> keyMaterial);

    /// <summary>
    /// Encrypts and stores a secret to the physical vault directory.
    /// </summary>
    /// <param name="name">The name of the secret.</param>
    /// <param name="plaintext">The cleartext data.</param>
    /// <param name="password">The master password.</param>
    void StoreSecret(string name, byte[] plaintext, SecureString password);

    /// <summary>
    /// Decrypts and loads a secret from the physical vault.
    /// </summary>
    /// <param name="name">The name of the secret.</param>
    /// <param name="password">The master password.</param>
    /// <returns>A SecureSecret containing the cleartext.</returns>
    SecureSecret? LoadSecret(string name, SecureString password);

    /// <summary>
    /// Decrypts and loads a secret from the physical vault using a raw password span.
    /// </summary>
    /// <param name="name">The name of the secret.</param>
    /// <param name="passwordBytes">Raw password bytes.</param>
    /// <returns>A SecureSecret containing the cleartext.</returns>
    SecureSecret? LoadSecret(string name, ReadOnlySpan<byte> passwordBytes);

    /// <summary>
    /// Protects a configuration string using a master key.
    /// </summary>
    /// <param name="secret">The configuration cleartext bytes.</param>
    /// <param name="masterKey">The master key material.</param>
    /// <returns>A protected URI string.</returns>
    string ProtectConfig(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> masterKey);

    /// <summary>
    /// Unprotects a configuration string using a master key.
    /// </summary>
    /// <param name="protectedSecret">A protected URI string (or a plain text string).</param>
    /// <param name="masterKey">The master key material.</param>
    /// <returns>The original configuration string.</returns>
    string? UnprotectConfig(string protectedSecret, ReadOnlySpan<byte> masterKey);

    /// <summary>
    /// Unprotects a configuration string using a master key into a secure buffer.
    /// </summary>
    /// <param name="protectedSecret">A protected URI string (or a plain text string).</param>
    /// <param name="masterKey">The master key material.</param>
    /// <returns>A SecureSecret containing the original configuration bytes.</returns>
    SecureSecret? UnprotectConfigToBytes(string protectedSecret, ReadOnlySpan<byte> masterKey);
}
