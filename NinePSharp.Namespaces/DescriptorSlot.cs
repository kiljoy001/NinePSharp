namespace NinePSharp.Namespaces;

/// <summary>A snapshot of a descriptor slot, not an additional channel reference.</summary>
public sealed record DescriptorSlot(int Number, ResourceOpenHandle Handle, bool CloseOnExec);
