using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Server.Utils;

public class VaultService
{
    private const int Iterations = 100000;
    private const int SaltSize = 16;
    private const int KeySize = 32; // AES-256

    public struct EncryptedPayload
    {
        public byte[] Salt { get; set; }
        public byte[] Nonce { get; set; }
        public byte[] Ciphertext { get; set; }
        public byte[] Tag { get; set; }

        public byte[] ToBytes()
        {
            var ms = new MemoryStream();
            ms.Write(Salt);
            ms.Write(Nonce);
            ms.Write(Tag);
            ms.Write(Ciphertext);
            return ms.ToArray();
        }

        public static EncryptedPayload FromBytes(byte[] data)
        {
            return new EncryptedPayload
            {
                Salt = data[..16],
                Nonce = data[16..28],
                Tag = data[28..44],
                Ciphertext = data[44..]
            };
        }
    }

    public static EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext, string password) => Encrypt(plaintext, Encoding.UTF8.GetBytes(password));

    public static EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(12); // GCM standard nonce size
        var key = DeriveKey(password, salt);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        try {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            return new EncryptedPayload
            {
                Salt = salt,
                Nonce = nonce,
                Ciphertext = ciphertext,
                Tag = tag
            };
        }
        finally {
            Array.Clear(key);
        }
    }

    public static SecureSecret DecryptToBytes(EncryptedPayload payload, string password) => DecryptToBytes(payload, Encoding.UTF8.GetBytes(password));

    public static SecureSecret DecryptToBytes(EncryptedPayload payload, ReadOnlySpan<byte> password)
    {
        var key = DeriveKey(password, payload.Salt);
        byte[] plaintextBytes = GC.AllocateArray<byte>(payload.Ciphertext.Length, pinned: true);

        unsafe {
            fixed (byte* p = plaintextBytes) {
                MemoryLock.Lock((IntPtr)p, (nuint)plaintextBytes.Length);
            }
        }

        try {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintextBytes);
            return new SecureSecret(plaintextBytes);
        }
        catch {
            Array.Clear(plaintextBytes);
            throw;
        }
        finally {
            Array.Clear(key);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt) => DeriveKey(Encoding.UTF8.GetBytes(password), salt);

    private static byte[] DeriveKey(ReadOnlySpan<byte> password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
