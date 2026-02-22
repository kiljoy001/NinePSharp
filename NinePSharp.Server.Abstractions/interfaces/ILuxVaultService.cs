using System;
using System.Security;

namespace NinePSharp.Server.Interfaces;

public interface ILuxVaultService
{
    string GetVaultPath(string fileName);

    // Hidden ID generation (Secret Pointers)
    string GenerateHiddenId(byte[] seed);
    byte[] DeriveSeed(string password, byte[] nonce);
    byte[] DeriveSeed(SecureString password, byte[] nonce);

    // Encryption / Decryption
    byte[] Encrypt(byte[] plaintextBytes, string password);
    byte[] Encrypt(byte[] plaintextBytes, SecureString password);
    byte[] Encrypt(string text, string password);
    byte[] Encrypt(string text, SecureString password);
    byte[] Encrypt(byte[] plaintextBytes, ReadOnlySpan<byte> key);
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> keyMaterial);
    
    byte[]? DecryptToBytes(byte[] payload, string password);
    byte[]? DecryptToBytes(byte[] payload, SecureString password);
    byte[]? DecryptToBytes(byte[] payload, ReadOnlySpan<byte> key);

    [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
    string? Decrypt(byte[] payload, string password);
    [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
    string? Decrypt(byte[] payload, SecureString password);
    [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
    string? Decrypt(byte[] payload, ReadOnlySpan<byte> key);

    // Config Protection (Master Key)
    string ProtectConfig(string secret, ReadOnlySpan<byte> masterKey);
    string? UnprotectConfig(string protectedSecret, ReadOnlySpan<byte> masterKey);
}
