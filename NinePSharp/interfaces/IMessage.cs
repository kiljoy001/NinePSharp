namespace NinePSharp.Interfaces;

using NinePSharp.Constants;

/// <summary>
/// Defines the base interface for all 9P protocol messages.
/// </summary>
public interface IMessage
{
    /// <summary>
    /// Gets the total size of the message in bytes, including the header.
    /// </summary>
    uint Size { get; }

    /// <summary>
    /// Gets the 9P message type.
    /// </summary>
    MessageTypes Type { get; }

    /// <summary>
    /// Gets the tag associated with this message.
    /// </summary>
    ushort Tag { get; }
}