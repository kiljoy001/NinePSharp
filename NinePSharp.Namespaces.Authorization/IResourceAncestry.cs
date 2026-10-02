namespace NinePSharp.Namespaces.Authorization;

/// <summary>
/// Provider-attested parent relation. Tree grants, read-only roots, remove and remove-on-close
/// need it; string prefixes and caller-supplied parents never establish containment.
/// </summary>
public interface IResourceAncestry
{
    /// <summary>Returns the resource's current parent directory, or null at a provider root.</summary>
    ValueTask<ResourceHandle?> GetParentAsync(ResourceHandle resource, CancellationToken cancellationToken);
}
