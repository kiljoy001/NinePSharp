using System;
using NinePSharp.Server.Utils;

var vault = new LuxVaultService();
var enc = vault.Encrypt("", "");
Console.WriteLine($"Enc length: {enc.Length}");

#pragma warning disable CS0618
var dec = vault.Decrypt(enc, "");
#pragma warning restore CS0618

Console.WriteLine($"Dec is null? {dec == null}");
if (dec != null) Console.WriteLine($"Dec: '{dec}'");
