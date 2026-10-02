using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>A service connection which can be mounted into a process namespace.</summary>
public sealed class NamespaceMountSource
{
    public NamespaceMountSource(
        NamespaceChannel root,
        byte mode,
        string? attachName = null,
        bool authenticated = true,
        bool requiresAuthentication = false,
        Func<ValueTask>? closeAsync = null)
    {
        Root = root?.Clone() ?? throw new ArgumentNullException(nameof(root));
        Mode = mode;
        AttachName = attachName ?? string.Empty;
        Authenticated = authenticated;
        RequiresAuthentication = requiresAuthentication;
        CloseAsync = closeAsync;
    }

    /// <summary>Gets the service root channel.</summary>
    public NamespaceChannel Root { get; }

    /// <summary>Gets the descriptor open mode.</summary>
    public byte Mode { get; }

    /// <summary>Gets the server tree selection string.</summary>
    public string AttachName { get; }

    /// <summary>Gets a value indicating whether the service connection is authenticated.</summary>
    public bool Authenticated { get; }

    /// <summary>Gets a value indicating whether the service requires authentication.</summary>
    public bool RequiresAuthentication { get; }

    /// <summary>Gets the callback which closes the caller's descriptor.</summary>
    public Func<ValueTask>? CloseAsync { get; }
}
