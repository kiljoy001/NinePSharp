using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Utils
{
    /// <inheritdoc />
    public class LuxVaultService : ILuxVaultService
    {
        /// <inheritdoc />
        public IEnumerable<SecureMemoryArena> Arenas => LuxVault.Arenas;

        /// <inheritdoc />
        public SecureMemoryArena GetLocalArena() => LuxVault.GetLocalArena();

        /// <inheritdoc />
        public string GetVaultPath(string fileName) => LuxVault.GetVaultPath(fileName);

        /// <inheritdoc />
        public string GenerateHiddenId(ReadOnlySpan<byte> seed) => LuxVault.GenerateHiddenId(seed);
        
        /// <inheritdoc />
        public void DeriveSeed(string password, ReadOnlySpan<byte> nonce, Span<byte> output) => LuxVault.DeriveSeed(password, nonce, output);
        
        /// <inheritdoc />
        public void DeriveSeed(SecureString password, ReadOnlySpan<byte> nonce, Span<byte> output) => LuxVault.DeriveSeed(password, nonce, output);

        /// <inheritdoc />
        public byte[] Encrypt(ReadOnlySpan<byte> plaintext, string password) => LuxVault.Encrypt(plaintext, password);
        
        /// <inheritdoc />
        public byte[] Encrypt(ReadOnlySpan<byte> plaintext, SecureString password) => LuxVault.Encrypt(plaintext, password);
        
        /// <inheritdoc />
        public byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> keyMaterial) => LuxVault.Encrypt(plaintext, keyMaterial);

        /// <inheritdoc />
        public SecureSecret? DecryptToBytes(byte[] payload, string password) => LuxVault.DecryptToBytes(payload, password);
        
        /// <inheritdoc />
        public SecureSecret? DecryptToBytes(byte[] payload, SecureString password) => LuxVault.DecryptToBytes(payload, password);
        
        /// <inheritdoc />
        public SecureSecret? DecryptToBytes(byte[] payload, ReadOnlySpan<byte> keyMaterial) => LuxVault.DecryptToBytes(payload, keyMaterial);

        /// <inheritdoc />
        public void StoreSecret(string name, byte[] plaintext, SecureString password) => LuxVault.StoreSecret(name, plaintext, password);

        /// <inheritdoc />
        public SecureSecret? LoadSecret(string name, SecureString password) => LuxVault.LoadSecret(name, password);

        /// <inheritdoc />
        public SecureSecret? LoadSecret(string name, ReadOnlySpan<byte> passwordBytes) => LuxVault.LoadSecret(name, passwordBytes);

        /// <inheritdoc />
        public string ProtectConfig(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> masterKey) => LuxVault.ProtectConfig(plaintext, masterKey);

        /// <inheritdoc />
        public string? UnprotectConfig(string secretUri, ReadOnlySpan<byte> masterKey) => LuxVault.UnprotectConfig(secretUri, masterKey);

        /// <inheritdoc />
        public SecureSecret? UnprotectConfigToBytes(string secretUri, ReadOnlySpan<byte> masterKey) => LuxVault.UnprotectConfigToBytes(secretUri, masterKey);
    }
}
