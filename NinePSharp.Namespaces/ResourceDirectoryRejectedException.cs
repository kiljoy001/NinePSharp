using System.Buffers.Binary;

namespace NinePSharp.Namespaces;

/// <summary>
/// An acknowledged directory provider rejection with no successful read to reconcile.
/// Transport failure, cancellation and lost replies must not use this exception.
/// </summary>
public sealed class ResourceDirectoryRejectedException(string message) : IOException(message);
