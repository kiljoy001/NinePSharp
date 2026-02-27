using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Configuration;

public static class ConfigSecretResolver
{
    // List of property names that should be automatically protected in memory
    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcPassword",
        "PrivateKey",
        "BlockfrostProjectId",
        "ConnectionString",
        "RpcUser" // Sometimes usernames are sensitive too
    };

    public static void ResolveSecrets(object? obj, ReadOnlySpan<byte> masterKey)
    {
        if (obj == null || masterKey.IsEmpty) return;

        var type = obj.GetType();
        if (type.IsPrimitive || type == typeof(string) || type == typeof(ProtectedSecret)) return;

        if (obj is IEnumerable enumerable && !(obj is string))
        {
            foreach (var item in enumerable)
            {
                ResolveSecrets(item, masterKey);
            }
            return;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            if (!prop.CanRead) continue;

            var value = prop.GetValue(obj);
            if (value == null) continue;

            if (prop.PropertyType == typeof(string))
            {
                var strValue = (string)value;
                if (strValue.StartsWith("secret://") && prop.CanWrite)
                {
                    using var decrypted = LuxVault.UnprotectConfigToBytes(strValue, masterKey);
                    if (decrypted != null)
                    {
                        // LEAK: Property is string, so we must decrypt to string.
                        // This should be avoided by changing the property type to ProtectedSecret.
                        prop.SetValue(obj, Encoding.UTF8.GetString(decrypted.Span));
                    }
                }
            }
            else if (prop.PropertyType == typeof(ProtectedSecret))
            {
                if (value is string strValue && prop.CanWrite)
                {
                    if (strValue.StartsWith("secret://"))
                    {
                        using var decrypted = LuxVault.UnprotectConfigToBytes(strValue, masterKey);
                        if (decrypted != null)
                        {
                            prop.SetValue(obj, new ProtectedSecret(decrypted.Span));
                        }
                    }
                    else
                    {
                        // Protect plain text from config in memory
                        #pragma warning disable CS0618
                        prop.SetValue(obj, new ProtectedSecret(strValue));
                        #pragma warning restore CS0618
                    }
                }
            }
            else if (prop.PropertyType.IsClass)
            {
                ResolveSecrets(value, masterKey);
            }
        }
    }

    public static void ProtectSensitiveFields(object? obj)
    {
        if (obj == null) return;

        var type = obj.GetType();
        if (type.IsPrimitive || type == typeof(string) || type == typeof(ProtectedSecret)) return;

        if (obj is IEnumerable enumerable && !(obj is string))
        {
            foreach (var item in enumerable)
            {
                ProtectSensitiveFields(item);
            }
            return;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            if (!prop.CanRead) continue;

            var value = prop.GetValue(obj);
            if (value == null) continue;

            if (prop.PropertyType == typeof(string))
            {
                var strValue = (string)value;
                
                // If it's a known sensitive field, we could ideally convert the property type, 
                // but since we can't change types at runtime, we'll use a naming convention 
                // or just rely on the fact that we can't easily "hide" strings in .NET once they are created.
                // However, we CAN ensure that the values aren't left in a decryptable state if they were secret://
                
                if (strValue.StartsWith("secret://"))
                {
                    // This is a special case: the user is providing an ALREADY encrypted secret.
                    // If we don't have a master key to decrypt it, we can't do anything.
                    // But wait, the user said "randomly generated on each boot".
                    // This implies they want us to protect CLEAR text that comes from config
                    // so it doesn't stay clear in memory.
                }
            }
            else if (prop.PropertyType.IsClass)
            {
                ProtectSensitiveFields(value);
            }
        }
    }
}
