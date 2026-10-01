namespace NinePSharp.Namespaces.Tests.Support;

internal sealed class ControlledDataPlane(INamespaceDataPlane inner) : INamespaceDataPlane
{
    internal Func<Task>? BeforeAttach { get; set; }
    internal Func<Task>? BeforeClunk { get; set; }
    internal Func<ResourceOpenHandle, Task>? AfterOpen { get; set; }
    internal Func<NamespaceChannel, byte, ResourceOperationContext, CancellationToken, ValueTask<ResourceOpenHandle>>? OpenOverride { get; set; }
    internal Func<ResourceOpenHandle, ResourceOperationContext, CancellationToken, ValueTask>? ClunkOverride { get; set; }
    internal Func<Task>? BeforeCreate { get; set; }
    internal Func<NamespaceCreateResult, Task>? AfterCreate { get; set; }
    internal Func<NamespaceChannel, CancellationToken, ValueTask<IReadOnlyList<ResourceStat>>>? DirectoryOverride { get; set; }
    internal Func<ResourceOpenHandle, ulong, uint, CancellationToken, ValueTask<ReadOnlyMemory<byte>>>? ReadOverride { get; set; }
    internal Func<ResourceOpenHandle, ulong, ReadOnlyMemory<byte>, ResourceOperationContext, CancellationToken, ValueTask<uint>>? WriteOverride { get; set; }

    public async ValueTask<NamespaceChannel> AttachAsync(ResourceHandle root, CancellationToken cancellationToken)
    {
        if (BeforeAttach is not null) await BeforeAttach();
        return await inner.AttachAsync(root, cancellationToken);
    }

    public ValueTask<NamespaceWalkResult> WalkAsync(NamespaceChannel source, IReadOnlyList<string> names, CancellationToken cancellationToken)
        => inner.WalkAsync(source, names, cancellationToken);
    public async ValueTask<ResourceOpenHandle> OpenAsync(NamespaceChannel channel, byte mode, ResourceOperationContext context, CancellationToken cancellationToken)
    {
        ResourceOpenHandle opened = OpenOverride is null
            ? await inner.OpenAsync(channel, mode, context, cancellationToken)
            : await OpenOverride(channel, mode, context, cancellationToken);
        if (AfterOpen is not null) await AfterOpen(opened);
        return opened;
    }
    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(ResourceOpenHandle handle, ulong offset, uint count, CancellationToken cancellationToken)
        => ReadOverride is null ? inner.ReadAsync(handle, offset, count, cancellationToken) : ReadOverride(handle, offset, count, cancellationToken);
    public ValueTask<IReadOnlyList<ResourceStat>> ReadDirectoryAsync(NamespaceChannel channel, CancellationToken cancellationToken)
        => DirectoryOverride is null ? inner.ReadDirectoryAsync(channel, cancellationToken) : DirectoryOverride(channel, cancellationToken);
    public ValueTask<uint> WriteAsync(ResourceOpenHandle handle, ulong offset, ReadOnlyMemory<byte> data, ResourceOperationContext context, CancellationToken cancellationToken)
        => WriteOverride is null ? inner.WriteAsync(handle, offset, data, context, cancellationToken) : WriteOverride(handle, offset, data, context, cancellationToken);
    public ValueTask<ResourceStat> StatAsync(NamespaceChannel channel, CancellationToken cancellationToken)
        => inner.StatAsync(channel, cancellationToken);
    public ValueTask<ResourceStat> StatAsync(ResourceOpenHandle handle, CancellationToken cancellationToken)
        => inner.StatAsync(handle, cancellationToken);
    public async ValueTask<NamespaceCreateResult> CreateAndOpenAsync(NamespaceChannel channel, string name, uint permissions, byte mode, ResourceOperationContext context, CancellationToken cancellationToken)
    {
        if (BeforeCreate is not null) await BeforeCreate();
        NamespaceCreateResult created = await inner.CreateAndOpenAsync(channel, name, permissions, mode, context, cancellationToken);
        if (AfterCreate is not null) await AfterCreate(created);
        return created;
    }
    public async ValueTask ClunkAsync(ResourceOpenHandle handle, ResourceOperationContext context, CancellationToken cancellationToken)
    {
        if (BeforeClunk is not null) await BeforeClunk();
        if (ClunkOverride is null) await inner.ClunkAsync(handle, context, cancellationToken);
        else await ClunkOverride(handle, context, cancellationToken);
    }
    public ValueTask RemoveAsync(NamespaceChannel channel, ResourceOpenHandle? handle, ResourceOperationContext context, CancellationToken cancellationToken)
        => inner.RemoveAsync(channel, handle, context, cancellationToken);
}
