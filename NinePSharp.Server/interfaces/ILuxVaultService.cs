using System;

namespace NinePSharp.Server.Interfaces;

public interface ILuxVaultService
{
    // Hidden ID generation (Secret Pointers)
    string GenerateHiddenId(byte[] seed);
    byte[] DeriveSeed(string password, byte[] nonce);

    // Encryption / Decryption
    byte[] Encrypt(byte[] plaintextBytes, string password);
    byte[] Encrypt(string text, string password);
    byte[]? DecryptToBytes(byte[] payload, string password);
    string? Decrypt(byte[] payload, string password);

    // Invisible Lock (Deniable Encryption)
    byte[] EncryptInvisible(byte[] plaintext, string password);
    byte[]? DecryptInvisible(byte[] payload, string password);

    // Config Protection (Master Key)
    string ProtectConfig(string secret, string masterKey);
    string? UnprotectConfig(string protectedSecret, string masterKey);
}
