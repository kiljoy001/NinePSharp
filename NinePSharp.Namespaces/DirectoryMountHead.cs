namespace NinePSharp.Namespaces;

// Identity survives replacement and selected unmount, but not complete unmount.
internal sealed class DirectoryMountHead(IReadOnlyList<MountBinding> members)
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    internal IReadOnlyList<MountBinding> Members { get; set; } = members;

    internal async Task RetireAsync()
    {
        await Gate.WaitAsync();
        try
        {
            Members = Array.Empty<MountBinding>();
        }
        finally
        {
            Gate.Release();
        }
    }
}
