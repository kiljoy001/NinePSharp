namespace NinePSharp.Namespaces.Authorization;

/// <summary>A definite authorization denial. The operation was not dispatched to its provider.</summary>
public sealed class ResourceAccessDeniedException : UnauthorizedAccessException
{
    public ResourceAccessDeniedException()
        : base("permission denied")
    {
    }
}
