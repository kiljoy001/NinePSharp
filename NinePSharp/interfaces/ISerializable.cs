namespace NinePSharp.Interfaces;

public interface ISerializable : IMessage
{
    void WriteTo(Span<byte> span);
}