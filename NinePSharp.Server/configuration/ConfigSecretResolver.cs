using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NinePSharp.Server.Utils;

namespace NinePSharp.Server.Configuration;

public static class ConfigSecretResolver
{
    public static void ResolveSecrets(object? obj, string? masterKey)
    {
        if (obj == null || string.IsNullOrEmpty(masterKey)) return;

        var type = obj.GetType();
        if (type.IsPrimitive || type == typeof(string)) return;

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
                    var decrypted = LuxVault.UnprotectConfig(strValue, masterKey);
                    if (decrypted != null)
                    {
                        prop.SetValue(obj, decrypted);
                    }
                }
            }
            else if (prop.PropertyType.IsClass)
            {
                ResolveSecrets(value, masterKey);
            }
        }
    }
}
