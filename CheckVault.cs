using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NinePSharp.Server.Utils;

public class CheckVault
{
    public static void Main()
    {
        string password = "test-password";
        string name = "test-secret";
        byte[] payload = Encoding.UTF8.GetBytes("This is a secret message.");

        // 1. Derive seed and hidden ID
        byte[] idSalt = Encoding.UTF8.GetBytes(name);
        byte[] seed = LuxVault.DeriveSeed(password, idSalt);
        string hiddenId = LuxVault.GenerateHiddenId(seed);

        // 2. Encrypt
        byte[] encrypted = LuxVault.Encrypt(payload, password);

        // 3. Save
        string filename = $"secret_{hiddenId}.vlt";
        File.WriteAllBytes(filename, encrypted);

        Console.WriteLine($"Filename: {filename}");
        Console.WriteLine($"File size: {encrypted.Length} bytes");
        Console.WriteLine($"Hex dump of first 32 bytes of filename part:");
        Console.WriteLine(hiddenId.Substring(0, 32));
    }
}
