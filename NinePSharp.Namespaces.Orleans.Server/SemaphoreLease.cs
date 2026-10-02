namespace NinePSharp.Namespaces.Orleans.Server;

internal sealed class SemaphoreLease : IDisposable
{
    private SemaphoreSlim? semaphore;

    private SemaphoreLease(SemaphoreSlim semaphore)
    {
        this.semaphore = semaphore;
    }

    public void Dispose()
        => Interlocked.Exchange(ref semaphore, null)?.Release();

    internal static async ValueTask<SemaphoreLease> EnterAsync(
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreLease(semaphore);
    }
}
