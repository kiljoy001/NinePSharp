using System.Buffers.Binary;
using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Native open flags, including the descriptor-local OCEXEC flag.</summary>
public readonly record struct Plan9OpenRequest(byte Mode)
{
    /// <summary>Gets a value indicating whether exec closes the newly allocated descriptor.</summary>
    public bool CloseOnExec => (Mode & NinePConstants.OCEXEC) != 0;

    /// <summary>Gets the mode passed to the provider, as in chan.c:Aopen.</summary>
    public byte ProviderMode => (byte)(Mode & ~NinePConstants.OCEXEC);

    internal void Validate()
    {
        const byte allowed = 3 | NinePConstants.OTRUNC | NinePConstants.OCEXEC | NinePConstants.ORCLOSE;
        if ((Mode & ~allowed) != 0)
        {
            throw new NamespaceFidException("invalid open mode");
        }
    }

    internal void ValidateResource(ResourceHandle resource)
    {
        if (resource.IsDirectory && (ProviderMode & ~NinePConstants.ORCLOSE) != NinePConstants.OREAD)
        {
            throw new NamespaceFidException("directories may only be opened for reading");
        }
    }
}
