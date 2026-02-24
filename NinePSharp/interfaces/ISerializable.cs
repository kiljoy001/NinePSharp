namespace NinePSharp.Interfaces;

/// <summary>
/// Defines an interface for 9P messages that can be serialized to a byte span.
/// </summary>
public interface ISerializable : IMessage
{
    /// <summary>
    /// Serializes the message to the provided byte span.
    /// </summary>
    /// <param name="span">The target span to write the message data to.</param>
    void WriteTo(Span<byte> span);
}