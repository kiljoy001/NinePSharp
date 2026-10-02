namespace NinePSharp.Namespaces;

/// <summary>The opened child and replacement channel produced by a successful create.</summary>
public sealed record NamespaceCreateResult(NamespaceChannel Channel, ResourceOpenHandle OpenHandle);
