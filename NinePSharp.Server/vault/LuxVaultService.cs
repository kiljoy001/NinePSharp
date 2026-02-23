using System;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Utils
{
    public class LuxVaultService : ILuxVaultService
    {
        public string GetVaultPath(string fileName) => LuxVault.GetVaultPath(fileName);

        public string GenerateHiddenId(byte[] seed) => LuxVault.GenerateHiddenId(seed);
        
        public byte[] DeriveSeed(string password, byte[] nonce) => LuxVault.DeriveSeed(password, nonce);
        public byte[] DeriveSeed(SecureString password, byte[] nonce) => LuxVault.DeriveSeed(password, nonce);

        public byte[] Encrypt(byte[] plaintextBytes, string password) => LuxVault.Encrypt(plaintextBytes, password);
        public byte[] Encrypt(byte[] plaintextBytes, SecureString password) => LuxVault.Encrypt(plaintextBytes, password);
        
        public byte[] Encrypt(string text, string password) => LuxVault.Encrypt(Encoding.UTF8.GetBytes(text), password);
        public byte[] Encrypt(string text, SecureString password) => LuxVault.Encrypt(Encoding.UTF8.GetBytes(text), password);
        
        public byte[] Encrypt(byte[] plaintextBytes, ReadOnlySpan<byte> key) => LuxVault.Encrypt(plaintextBytes, key);
        public byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> keyMaterial) => LuxVault.Encrypt(plaintext, keyMaterial);

        public SecureSecret? DecryptToBytes(byte[] payload, string password) => LuxVault.DecryptToBytes(payload, password);
        public SecureSecret? DecryptToBytes(byte[] payload, SecureString password) => LuxVault.DecryptToBytes(payload, password);
        
        public SecureSecret? DecryptToBytes(byte[] payload, ReadOnlySpan<byte> key) => LuxVault.DecryptToBytes(payload, key);

        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
        public string? Decrypt(byte[] payload, string password) => LuxVault.Decrypt(payload, password);
        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
        public string? Decrypt(byte[] payload, SecureString password) => LuxVault.Decrypt(payload, password);
        
        [Obsolete("Use DecryptToBytes to avoid leaking secrets into the managed string pool.")]
        public string? Decrypt(byte[] payload, ReadOnlySpan<byte> key) => LuxVault.Decrypt(payload, key);

        public string ProtectConfig(string plainText, ReadOnlySpan<byte> masterKey) => LuxVault.ProtectConfig(plainText, masterKey);

        public string? UnprotectConfig(string secretUri, ReadOnlySpan<byte> masterKey) => LuxVault.UnprotectConfig(secretUri, masterKey);
    }
}
