using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>Maps an encoded directory device to its provider and provider-local device name.</summary>
public sealed record DirectoryDeviceBinding(ushort Type, uint Device, string Provider, string ResourceDevice);
