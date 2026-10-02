namespace NinePSharp.Namespaces.Authorization;

/// <summary>What a grant covers.</summary>
public enum GrantScope
{
    /// <summary>Exactly the named resource.</summary>
    Self,

    /// <summary>The named resource and its provider-attested descendants.</summary>
    Tree,
}
