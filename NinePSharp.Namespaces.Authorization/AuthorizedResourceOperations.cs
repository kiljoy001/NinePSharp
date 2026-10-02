using System.Collections.Concurrent;
using NinePSharp.Constants;

namespace NinePSharp.Namespaces.Authorization;

/// <summary>
/// One principal's authorized view of a provider. Every operation must be allowed by each layer:
/// an enabled principal under the current policy generation, a user or group grant, the 9front
/// gefs mode check, and the read-only restriction of the mount it is reached through.
/// </summary>
/// <remarks>
/// Rules follow 9front sys/src/cmd/gefs/fs.c (fsaccess, ingroup, mode2bits, fswalk, fscreate,
/// fsremove) and the remove-on-close parent check of hjfs fs2.c. An object on which the principal
/// holds no right is indistinguishable from an absent one. Metadata updates (wstat) are not
/// exposed: this type deliberately does not implement the provider wstat or open-stat interfaces.
/// </remarks>
public sealed class AuthorizedResourceOperations : IResourceDataOperations
{
    /// <summary>The longest parent chain followed; longer chains confer no containment.</summary>
    public const int MaxAncestryDepth = 64;

    /// <summary>gefs: the user that receives only the other permission set.</summary>
    public const string NoneUser = "none";

    /// <summary>gefs: members of this group do not receive the other permission set.</summary>
    public const string NoGroup = "nogroup";

    private const uint ReadBit = 4;
    private const uint WriteBit = 2;
    private const uint ExecuteBit = 1;

    private readonly IResourceDataOperations inner;
    private readonly IResourceAncestry? ancestry;
    private readonly AuthorizationPolicy policy;
    private readonly string user;
    private readonly Func<ulong> currentGeneration;
    private readonly IReadOnlyList<ResourceGrant> grants;
    private readonly HashSet<ResourceIdentity> readOnlyRoots;
    private readonly bool needsContainment;
    private readonly ConcurrentDictionary<ResourceOpenHandle, OpenGrant> opened = new();

    /// <summary>Initializes a new instance of the <see cref="AuthorizedResourceOperations"/> class. The view is for one authenticated principal.</summary>
    /// <param name="inner">The provider the namespace data plane would otherwise call.</param>
    /// <param name="ancestry">The provider's parent relation, or null when it has none.</param>
    /// <param name="policy">The validated policy generation this view enforces.</param>
    /// <param name="user">The trusted, authenticated principal.</param>
    /// <param name="currentGeneration">Returns the host's current policy generation.</param>
    /// <param name="readOnlyRoots">Roots of mounts that this view reaches read-only.</param>
    public AuthorizedResourceOperations(
        IResourceDataOperations inner,
        IResourceAncestry? ancestry,
        AuthorizationPolicy policy,
        string user,
        Func<ulong> currentGeneration,
        IEnumerable<ResourceIdentity>? readOnlyRoots = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        if (!policy.Contains(user))
        {
            throw new ArgumentException($"'{user}' is not a principal of this policy.", nameof(user));
        }

        this.currentGeneration = currentGeneration ?? throw new ArgumentNullException(nameof(currentGeneration));
        this.ancestry = ancestry;
        this.user = user;
        grants = policy.GrantsFor(user);
        this.readOnlyRoots = new HashSet<ResourceIdentity>(readOnlyRoots ?? Array.Empty<ResourceIdentity>());
        needsContainment = this.readOnlyRoots.Count != 0 || grants.Any(grant => grant.Scope == GrantScope.Tree);
        if (needsContainment && ancestry is null)
        {
            throw new ArgumentException("Tree grants and read-only roots need the provider's parent relation.", nameof(ancestry));
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceHandle?> WalkAsync(ResourceHandle directory, string name, CancellationToken cancellationToken)
    {
        RequireCurrent();
        Access access = await AccessAsync(directory, cancellationToken);

        // A hidden union member behaves as if it lacked the name.
        if (access.Rights == ResourceRights.None)
        {
            return null;
        }

        if ((access.Rights & ResourceRights.Walk) == 0)
        {
            throw new ResourceAccessDeniedException();
        }

        ResourceStat stat = await inner.StatAsync(directory, cancellationToken);
        if (!Permits(stat, ExecuteBit))
        {
            throw new ResourceAccessDeniedException();
        }

        ResourceHandle? child = await inner.WalkAsync(directory, name, cancellationToken);
        if (child is null)
        {
            return null;
        }

        return (await AccessAsync(child, cancellationToken)).Rights == ResourceRights.None ? null : child;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(ResourceHandle directory, CancellationToken cancellationToken)
    {
        RequireCurrent();
        Access access = await AccessAsync(directory, cancellationToken);
        if (access.Rights == ResourceRights.None)
        {
            return Array.Empty<ResourceDirectoryEntry>();
        }

        if ((access.Rights & ResourceRights.Read) == 0)
        {
            throw new ResourceDirectoryRejectedException("permission denied");
        }

        ResourceStat stat = await inner.StatAsync(directory, cancellationToken);
        if (!Permits(stat, ReadBit))
        {
            throw new ResourceDirectoryRejectedException("permission denied");
        }

        var visible = new List<ResourceDirectoryEntry>();
        foreach (ResourceDirectoryEntry entry in await inner.ReadDirectoryAsync(directory, cancellationToken))
        {
            if (((await AccessAsync(entry.Handle, cancellationToken)).Rights & ResourceRights.Stat) != 0)
            {
                visible.Add(entry);
            }
        }

        return visible;
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceHandle> CreateAsync(ResourceHandle directory, string name, bool directoryEntry, CancellationToken cancellationToken)
    {
        (_, _) = await AuthorizeCreateAsync(directory, ResourceRights.None, cancellationToken);
        return await inner.CreateAsync(directory, name, directoryEntry, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceOpenHandle> OpenAsync(
        ResourceHandle resource,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        bool directory = resource.IsDirectory;
        if (!Current())
        {
            throw Denied(directory);
        }

        Access access = await AccessAsync(resource, cancellationToken);
        if (access.Rights == ResourceRights.None)
        {
            throw Hidden(directory);
        }

        (ResourceRights needed, uint bits, bool mutates) = Requirements(mode);
        if ((access.Rights & needed) != needed)
        {
            throw Denied(directory);
        }

        ResourceStat stat = await inner.StatAsync(resource, cancellationToken);
        if (!Permits(stat, bits))
        {
            throw Denied(directory);
        }

        if (mutates && access.ReadOnly)
        {
            throw Denied(directory);
        }

        if ((mode & NinePConstants.ORCLOSE) != 0 && !await ParentPermitsWriteAsync(resource, cancellationToken))
        {
            throw Denied(directory);
        }

        ResourceOpenHandle handle = await inner.OpenAsync(resource, mode, context, cancellationToken);
        opened[handle] = OpenGrant.For(mode);
        return handle;
    }

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(ResourceOpenHandle openHandle, ulong offset, uint count, CancellationToken cancellationToken)
    {
        RequireCurrent();
        if (!opened.TryGetValue(openHandle, out OpenGrant grant) || !grant.Read)
        {
            throw new ResourceAccessDeniedException();
        }

        return inner.ReadAsync(openHandle, offset, count, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<uint> WriteAsync(
        ResourceOpenHandle openHandle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        RequireCurrent();
        if (!opened.TryGetValue(openHandle, out OpenGrant grant) || !grant.Write)
        {
            throw new ResourceAccessDeniedException();
        }

        return inner.WriteAsync(openHandle, offset, data, context, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceStat> StatAsync(ResourceHandle resource, CancellationToken cancellationToken)
    {
        RequireCurrent();
        Access access = await AccessAsync(resource, cancellationToken);
        if (access.Rights == ResourceRights.None)
        {
            throw Hidden(directory: false);
        }

        // stat(5) and gefs fsstat need no mode permission; the grant alone decides.
        if ((access.Rights & ResourceRights.Stat) == 0)
        {
            throw new ResourceAccessDeniedException();
        }

        return await inner.StatAsync(resource, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceOpenHandle> CreateAndOpenAsync(
        ResourceHandle directory,
        string name,
        uint permissions,
        byte mode,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        (ResourceRights needed, _, _) = Requirements(mode);
        (_, ResourceStat parent) = await AuthorizeCreateAsync(directory, needed, cancellationToken);

        // gefs fscreate: the child's permission bits are limited by the parent's.
        uint masked = (permissions & (uint)NinePConstants.FileMode9P.DMDIR) != 0
            ? permissions & (~0x1FFU | (parent.Mode & 0x1FF))
            : permissions & (~0x1B6U | (parent.Mode & 0x1B6));
        ResourceOpenHandle handle = await inner.CreateAndOpenAsync(directory, name, masked, mode, context, cancellationToken);
        opened[handle] = OpenGrant.For(mode);
        return handle;
    }

    /// <inheritdoc/>
    public ValueTask ClunkAsync(ResourceOpenHandle openHandle, ResourceOperationContext context, CancellationToken cancellationToken)
    {
        // Release is permitted after revocation, but only for handles this view opened.
        if (!opened.TryRemove(openHandle, out _))
        {
            throw new ResourceAccessDeniedException();
        }

        return inner.ClunkAsync(openHandle, context, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask RemoveAsync(
        ResourceHandle resource,
        ResourceOpenHandle? openHandle,
        ResourceOperationContext context,
        CancellationToken cancellationToken)
    {
        // remove(5): the fid is gone whether or not the remove succeeds.
        if (openHandle is not null)
        {
            opened.TryRemove(openHandle, out _);
        }

        RequireCurrent();
        Access access = await AccessAsync(resource, cancellationToken);
        if (access.Rights == ResourceRights.None)
        {
            throw Hidden(directory: false);
        }

        if ((access.Rights & ResourceRights.Remove) == 0 || access.ReadOnly)
        {
            throw new ResourceAccessDeniedException();
        }

        if (!await ParentPermitsWriteAsync(resource, cancellationToken))
        {
            throw new ResourceAccessDeniedException();
        }

        await inner.RemoveAsync(resource, openHandle, context, cancellationToken);
    }

    /// <summary>gefs mode2bits plus grant rights: OEXEC needs read and execute; OTRUNC adds write.</summary>
    private static (ResourceRights Rights, uint Bits, bool Mutates) Requirements(byte mode)
    {
        bool truncate = (mode & NinePConstants.OTRUNC) != 0;
        bool removeOnClose = (mode & NinePConstants.ORCLOSE) != 0;
        (ResourceRights rights, uint bits, bool writes) = (mode & 3) switch
        {
            NinePConstants.OREAD => (ResourceRights.Read, ReadBit, false),
            NinePConstants.OWRITE => (ResourceRights.Write, WriteBit, true),
            NinePConstants.ORDWR => (ResourceRights.Read | ResourceRights.Write, ReadBit | WriteBit, true),
            _ => (ResourceRights.Read, ReadBit | ExecuteBit, false),
        };
        ResourceRights extra = (truncate ? ResourceRights.Write : ResourceRights.None) |
            (removeOnClose ? ResourceRights.Remove : ResourceRights.None);
        return (rights | extra, truncate ? bits | WriteBit : bits, writes || truncate || removeOnClose);
    }

    private static Exception Denied(bool directory)
        => directory ? new ResourceDirectoryRejectedException("permission denied") : new ResourceAccessDeniedException();

    private static Exception Hidden(bool directory)
        => directory
            ? new ResourceDirectoryRejectedException("file does not exist")
            : new NamespaceException(NamespaceError.ResourceNotFound, "file does not exist");

    private async ValueTask<(Access Access, ResourceStat Parent)> AuthorizeCreateAsync(
        ResourceHandle directory, ResourceRights openRights, CancellationToken cancellationToken)
    {
        var rejected = new ResourceCreateRejectedException("permission denied");
        if (!Current())
        {
            throw rejected;
        }

        Access access = await AccessAsync(directory, cancellationToken);
        if ((access.Rights & ResourceRights.Create) == 0 || access.ReadOnly)
        {
            throw rejected;
        }

        // The child inherits only tree grants that cover its parent; it must be visible and openable.
        if (access.Inherited == ResourceRights.None || (access.Inherited & openRights) != openRights)
        {
            throw rejected;
        }

        ResourceStat parent = await inner.StatAsync(directory, cancellationToken);
        if (!Permits(parent, WriteBit))
        {
            throw rejected;
        }

        return (access, parent);
    }

    private async ValueTask<bool> ParentPermitsWriteAsync(ResourceHandle resource, CancellationToken cancellationToken)
    {
        if (ancestry is null)
        {
            return false;
        }

        ResourceHandle? parent = await ancestry.GetParentAsync(resource, cancellationToken);
        return parent is not null && Permits(await inner.StatAsync(parent, cancellationToken), WriteBit);
    }

    /// <summary>9front gefs fsaccess: owner set, then group set for members, then the other set.</summary>
    private bool Permits(ResourceStat stat, uint bits)
    {
        uint mode = stat.Mode;
        if (!string.Equals(user, NoneUser, StringComparison.Ordinal))
        {
            if (string.Equals(stat.User, user, StringComparison.Ordinal) && (mode & (bits << 6)) == bits << 6)
            {
                return true;
            }

            if (policy.InGroup(user, stat.Group) && (mode & (bits << 3)) == bits << 3)
            {
                return true;
            }
        }

        if ((mode & bits) != bits)
        {
            return false;
        }

        // gefs: nogroup members keep the other set only to search a directory.
        bool search = bits == ExecuteBit && (mode & (uint)NinePConstants.FileMode9P.DMDIR) != 0;
        return search || !policy.InGroup(user, NoGroup);
    }

    private bool Current() => currentGeneration() == policy.Generation && policy.IsEnabled(user);

    private void RequireCurrent()
    {
        if (!Current())
        {
            throw new ResourceAccessDeniedException();
        }
    }

    private async ValueTask<Access> AccessAsync(ResourceHandle resource, CancellationToken cancellationToken)
    {
        IReadOnlyList<ResourceIdentity>? chain = await AncestorsAsync(resource, cancellationToken);
        IReadOnlyList<ResourceIdentity> ancestors = chain ?? Array.Empty<ResourceIdentity>();
        var rights = ResourceRights.None;
        var inherited = ResourceRights.None;
        foreach (ResourceGrant grant in grants)
        {
            bool self = grant.Resource == resource.Identity;
            if (self && grant.Scope == GrantScope.Self)
            {
                rights |= grant.Rights;
            }

            if (grant.Scope == GrantScope.Tree && (self || ancestors.Contains(grant.Resource)))
            {
                rights |= grant.Rights;
                inherited |= grant.Rights;
            }
        }

        // An unproven chain gives no containment, so it cannot rule out a read-only root.
        bool readOnly = readOnlyRoots.Contains(resource.Identity) ||
            (chain is null ? readOnlyRoots.Count != 0 : chain.Any(readOnlyRoots.Contains));
        return new Access(rights, inherited, readOnly);
    }

    /// <summary>
    /// The provider-attested ancestors, nearest first, or null when containment is unproven:
    /// a parent cycle or more than <see cref="MaxAncestryDepth"/> ancestors.
    /// </summary>
    private async ValueTask<IReadOnlyList<ResourceIdentity>?> AncestorsAsync(ResourceHandle resource, CancellationToken cancellationToken)
    {
        if (!needsContainment)
        {
            return Array.Empty<ResourceIdentity>();
        }

        var chain = new List<ResourceIdentity>();
        var seen = new HashSet<ResourceIdentity>();
        ResourceHandle current = resource;
        while (true)
        {
            ResourceHandle? parent = await ancestry!.GetParentAsync(current, cancellationToken);
            if (parent is null)
            {
                return chain;
            }

            if (chain.Count == MaxAncestryDepth || !seen.Add(parent.Identity))
            {
                return null;
            }

            chain.Add(parent.Identity);
            current = parent;
        }
    }

    private readonly record struct Access(ResourceRights Rights, ResourceRights Inherited, bool ReadOnly);

    private readonly record struct OpenGrant(bool Read, bool Write)
    {
        internal static OpenGrant For(byte mode) => (mode & 3) switch
        {
            NinePConstants.OWRITE => new(false, true),
            NinePConstants.ORDWR => new(true, true),
            _ => new(true, false),
        };
    }
}
