using NinePSharp.Constants;

namespace NinePSharp.Namespaces;

/// <summary>Native create arguments; Mode retains the full OEXCL bit beyond the 9P byte mode.</summary>
public readonly record struct Plan9CreateRequest(uint Permissions, int Mode)
{
    /// <summary>Gets a value indicating whether an existing name must cause failure.</summary>
    public bool Exclusive => (Mode & NinePConstants.OEXCL) != 0;

    internal Plan9OpenRequest OpenRequest
    {
        get
        {
            const int allowed = 3 | NinePConstants.OTRUNC | NinePConstants.OCEXEC | NinePConstants.ORCLOSE | NinePConstants.OEXCL;
            if ((Mode & ~allowed) != 0)
            {
                throw new NamespaceFidException("invalid create mode");
            }

            return new((byte)(Mode & ~NinePConstants.OEXCL));
        }
    }

    internal bool IsDirectory => (Permissions & (uint)NinePConstants.FileMode9P.DMDIR) != 0;
}

/// <summary>
/// A provider's acknowledged create rejection: no handle was returned and this
/// request did not create the name. Only definite rejections permit native fallback;
/// transport failures, timeouts and cancellation must not use this exception.
/// </summary>
public sealed class ResourceCreateRejectedException : IOException
{
    public ResourceCreateRejectedException(string message)
        : base(message)
    {
    }
}
