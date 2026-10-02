namespace NinePSharp.Namespaces;

/// <summary>Use an asynchronous mount operation when a directory stream owns the head.</summary>
public sealed class NamespaceMutationBusyException() : InvalidOperationException(
    "Directory IO retains this mount head; use the asynchronous mount operation.");
