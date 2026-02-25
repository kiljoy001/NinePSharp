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
    /// Selects an arena shard based on the current thread ID.
    /// </summary>
    SecureMemoryArena GetLocalArena();

    /// <summary>
    /// Gets the physical path for a vault file.
    /// </summary>
    string GetVaultPath(string fileName);

    /// <summary>
    /// Generates a deterministic hidden identifier from a seed.
    /// </summary>
    string GenerateHiddenId(ReadOnlySpan<byte> seed);

    /// <summary>
    /// Derives a 32-byte seed from a password and nonce.
    /// </summary>
    void DeriveSeed(string password, ReadOnlySpan<byte> nonce, Span<byte> output);

    /// <summary>
    /// Derives a 32-byte seed from a SecureString password and nonce.
    /// </summary>
    void DeriveSeed(SecureString password, ReadOnlySpan<byte> nonce, Span<byte> output);

    /// <summary>
    /// Encrypts plaintext using a password.
    /// </summary>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, string password);

    /// <summary>
    /// Encrypts plaintext using a SecureString password.
    /// </summary>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, SecureString password);

    /// <summary>
    /// Encrypts plaintext using raw key material.
    /// </summary>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> keyMaterial);
    
    /// <summary>
    /// Decrypts a payload into a secure, pinned memory buffer.
    /// </summary>
    SecureSecret? DecryptToBytes(byte[] payload, string password);

    /// <summary>
    /// Decrypts a payload into a secure, pinned memory buffer using a SecureString password.
    /// </summary>
    SecureSecret? DecryptToBytes(byte[] payload, SecureString password);

    /// <summary>
    /// Decrypts a payload into a secure, pinned memory buffer using raw key material.
    /// </summary>
    SecureSecret? DecryptToBytes(byte[] payload, ReadOnlySpan<byte> keyMaterial);

    [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
    string? Decrypt(byte[] payload, string password);
    [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
    string? Decrypt(byte[] payload, SecureString password);
    [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
    string? Decrypt(byte[] payload, ReadOnlySpan<byte> key);

    /// <summary>
    /// Encrypts and stores a secret to the physical vault directory.
    /// </summary>
    void StoreSecret(string name, byte[] plaintext, SecureString password);

    /// <summary>
    /// Decrypts and loads a secret from the physical vault.
    /// </summary>
    SecureSecret? LoadSecret(string name, SecureString password);

    /// <summary>
    /// Decrypts and loads a secret from the physical vault using a raw password span.
    /// </summary>
    SecureSecret? LoadSecret(string name, ReadOnlySpan<byte> passwordBytes);

    /// <summary>
    /// Protects a configuration string using a master key.
    /// </summary>
    string ProtectConfig(string secret, ReadOnlySpan<byte> masterKey);

    /// <summary>
    /// Unprotects a configuration string using a master key.
    /// </summary>
    string? UnprotectConfig(string protectedSecret, ReadOnlySpan<byte> masterKey);
}
