namespace NinePSharp.Namespaces;

/// <summary>Identifies a namespace operation failure.</summary>
public enum NamespaceError
{
    /// <summary>The requested control operation is invalid.</summary>
    InvalidOperation,

    /// <summary>The mount flags are invalid.</summary>
    InvalidMountFlags,

    /// <summary>The source and target object kinds cannot be mounted together.</summary>
    MountTypeMismatch,

    /// <summary>A union mount was attempted on a non-directory.</summary>
    UnionRequiresDirectory,

    /// <summary>A service mount was attempted on a non-directory target.</summary>
    MountTargetMustBeDirectory,

    /// <summary>The requested mount point does not exist.</summary>
    MountNotFound,

    /// <summary>The requested union member does not exist.</summary>
    UnionMemberNotFound,

    /// <summary>No union member permits creation.</summary>
    CreateNotPermitted,

    /// <summary>A creatable bind was attempted from an incompatible mounted source.</summary>
    CreateBindNotPermitted,

    /// <summary>The process namespace is not permitted to mount this device.</summary>
    MountDeviceDenied,

    /// <summary>A service descriptor was not opened read-write.</summary>
    MountSourceNotReadWrite,

    /// <summary>A service requires authentication that was not supplied.</summary>
    MountAuthenticationRequired,

    /// <summary>A path component could not be resolved.</summary>
    ResourceNotFound,

    /// <summary>The process or namespace group has released its ownership.</summary>
    NamespaceClosed,

    /// <summary>A path ending in a slash or dot selected a regular file.</summary>
    ResourceNotDirectory,

    /// <summary>Exclusive creation found an existing namespace entry.</summary>
    ResourceAlreadyExists,
}
