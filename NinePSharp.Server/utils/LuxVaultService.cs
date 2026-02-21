using System;
using System.Security.Cryptography;
using System.Text;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Utils
{
    public class LuxVaultService : ILuxVaultService
    {
        public string GenerateHiddenId(byte[] seed) => LuxVault.GenerateHiddenId(seed);
        
        public byte[] DeriveSeed(string password, byte[] nonce) => LuxVault.DeriveSeed(password, nonce);

        public byte[] Encrypt(byte[] plaintextBytes, string password) => LuxVault.Encrypt(plaintextBytes, password);

        public byte[] Encrypt(string text, string password) => LuxVault.Encrypt(text, password);

        public byte[]? DecryptToBytes(byte[] payload, string password) => LuxVault.DecryptToBytes(payload, password);

        public string? Decrypt(byte[] payload, string password) => LuxVault.Decrypt(payload, password);

        public string ProtectConfig(string plainText, string masterKey) => LuxVault.ProtectConfig(plainText, masterKey);

        public string? UnprotectConfig(string secretUri, string masterKey) => LuxVault.UnprotectConfig(secretUri, masterKey);
    }
}
