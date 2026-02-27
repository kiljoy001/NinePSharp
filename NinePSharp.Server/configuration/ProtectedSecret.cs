using System;
using System.Runtime.InteropServices;
using System.Security;
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
        
        // Memory-lock the static session key
        unsafe {
            fixed (byte* pKey = _sessionKey) {
                MemoryLock.Lock((IntPtr)pKey, (nuint)_sessionKey.Length);
            }
        }
    }

    [Obsolete("Use the SecureString or ReadOnlySpan<byte> constructor to prevent cleartext leakage into the managed heap.")]
    public ProtectedSecret(string clearText)
    {
        if (_sessionKey == null) throw new InvalidOperationException("Session key not initialized.");
        
        byte[] clearBytes = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(clearText), pinned: true);
        Encoding.UTF8.GetBytes(clearText, 0, clearText.Length, clearBytes, 0);
        
        unsafe {
            fixed (byte* pClear = clearBytes) {
                MemoryLock.Lock((IntPtr)pClear, (nuint)clearBytes.Length);
            }
        }
        
        try
        {
            _encryptedData = LuxVault.Encrypt(clearBytes, _sessionKey);
        }
        finally
        {
            unsafe {
                fixed (byte* pClear = clearBytes) {
                    MemoryLock.Unlock((IntPtr)pClear, (nuint)clearBytes.Length);
                }
            }
            Array.Clear(clearBytes);
        }
    }

    public ProtectedSecret(SecureString secureString)
    {
        if (_sessionKey == null) throw new InvalidOperationException("Session key not initialized.");
        
        IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
        nuint unmanagedLen = (nuint)(secureString.Length * 2);
        MemoryLock.Lock(ptr, unmanagedLen);
        
        try
        {
            unsafe {
                int length = secureString.Length;
                char* chars = (char*)ptr.ToPointer();
                int byteCount = Encoding.UTF8.GetByteCount(chars, length);
                if (byteCount == 0)
                {
                    _encryptedData = LuxVault.Encrypt(Array.Empty<byte>(), _sessionKey);
                    return;
                }
                byte[] clearBytes = GC.AllocateArray<byte>(byteCount, pinned: true);
                
                fixed (byte* pBytes = clearBytes) {
                    MemoryLock.Lock((IntPtr)pBytes, (nuint)clearBytes.Length);
                }
                
                try {
                    fixed (byte* pBytes = clearBytes)
                    {
                        Encoding.UTF8.GetBytes(chars, length, pBytes, byteCount);
                    }
                    _encryptedData = LuxVault.Encrypt(clearBytes, _sessionKey);
                }
                finally {
                    fixed (byte* pBytes = clearBytes) {
                        MemoryLock.Unlock((IntPtr)pBytes, (nuint)clearBytes.Length);
                    }
                    Array.Clear(clearBytes);
                }
            }
        }
        finally
        {
            MemoryLock.Unlock(ptr, unmanagedLen);
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    public ProtectedSecret(ReadOnlySpan<byte> clearBytes)
    {
        if (_sessionKey == null) throw new InvalidOperationException("Session key not initialized.");
        _encryptedData = LuxVault.Encrypt(clearBytes, _sessionKey);
    }

    /// <summary>
    /// Reveal the secret only for the duration of the provided action.
    /// The decrypted buffer is explicitly zeroed out immediately after the action completes.
    /// </summary>
    public void Use(Action<ReadOnlySpan<byte>> action)
    {
        ObjectDisposedException.ThrowIf(_encryptedData == null, this);
        if (_sessionKey == null) throw new InvalidOperationException("Session key not initialized.");

        using var secret = LuxVault.DecryptToBytes(_encryptedData, _sessionKey);
        if (secret != null)
        {
            action(secret.Span);
        }
    }

    /// <summary>
    /// Asynchronously reveal the secret only for the duration of the provided action.
    /// The decrypted buffer is explicitly zeroed out immediately after the action completes.
    /// </summary>
    public async Task UseAsync(Func<ReadOnlyMemory<byte>, Task> action)
    {
        ObjectDisposedException.ThrowIf(_encryptedData == null, this);
        if (_sessionKey == null) throw new InvalidOperationException("Session key not initialized.");

        // Note: SecureSecret must be fully consumed before disposal.
        // We copy to a pinned temporary for the async boundary, then dispose both.
        using var secret = LuxVault.DecryptToBytes(_encryptedData, _sessionKey);
        if (secret != null)
        {
            // Copy into a temporary pinned array for the async boundary
            // (ReadOnlySpan cannot cross await)
            byte[] temp = GC.AllocateArray<byte>(secret.Length, pinned: true);
            try
            {
                secret.Span.CopyTo(temp);
                await action(temp.AsMemory());
            }
            finally
            {
                Array.Clear(temp);
            }
        }
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
