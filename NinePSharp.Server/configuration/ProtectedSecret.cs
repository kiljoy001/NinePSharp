using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Configuration;

/// <summary>
/// A memory-protected secret that is encrypted with the boot-time session key.
/// Implements IDisposable to ensure encrypted buffers are zeroed out.
/// </summary>
public sealed class ProtectedSecret : IDisposable
{
    private byte[]? _encryptedData;
    
    // Store static session key in a pinned array to prevent GC moves
    private static byte[]? _sessionKey;

    public static void InitializeSessionKey(ReadOnlySpan<byte> sessionKey)
    {
        if (_sessionKey != null) return;
        
        // Allocate pinned array for session key
        _sessionKey = GC.AllocateArray<byte>(sessionKey.Length, pinned: true);
        sessionKey.CopyTo(_sessionKey);
    }

    public ProtectedSecret(string clearText)
    {
        if (_sessionKey == null) throw new InvalidOperationException("Session key not initialized.");
        
        byte[] clearBytes = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(clearText), pinned: true);
        Encoding.UTF8.GetBytes(clearText, 0, clearText.Length, clearBytes, 0);
        
        try
        {
            _encryptedData = LuxVault.Encrypt(clearBytes, _sessionKey);
        }
        finally
        {
            Array.Clear(clearBytes);
        }
    }

    /// <summary>
    /// Reveal the secret only for the duration of the provided action.
    /// The decrypted buffer is explicitly zeroed out immediately after the action completes.
    /// </summary>
    public void Use(Action<ReadOnlySpan<byte>> action)
    {
        if (_encryptedData == null || _sessionKey == null) return;

        byte[]? decrypted = null;
        try
        {
            decrypted = LuxVault.DecryptToBytes(_encryptedData, _sessionKey);
            if (decrypted != null)
            {
                action(decrypted);
            }
        }
        finally
        {
            if (decrypted != null)
            {
                Array.Clear(decrypted);
            }
        }
    }

    /// <summary>
    /// Asynchronously reveal the secret only for the duration of the provided action.
    /// The decrypted buffer is explicitly zeroed out immediately after the action completes.
    /// </summary>
    public async Task UseAsync(Func<ReadOnlyMemory<byte>, Task> action)
    {
        if (_encryptedData == null || _sessionKey == null) return;

        byte[]? decrypted = null;
        try
        {
            decrypted = LuxVault.DecryptToBytes(_encryptedData, _sessionKey);
            if (decrypted != null)
            {
                await action(decrypted.AsMemory());
            }
        }
        finally
        {
            if (decrypted != null)
            {
                Array.Clear(decrypted);
            }
        }
    }

    /// <summary>
    /// Legacy reveal as string. DISCOURAGED. 
    /// This will leak cleartext into the managed string pool.
    /// </summary>
    [Obsolete("Use the Use() method to prevent cleartext leakage into the managed heap.")]
    public string? Reveal()
    {
        string? result = null;
        Use(bytes => {
            result = Encoding.UTF8.GetString(bytes);
        });
        return result;
    }

    public void Dispose()
    {
        if (_encryptedData != null)
        {
            Array.Clear(_encryptedData);
            _encryptedData = null;
        }
    }

    public override string ToString() => "********";
}
