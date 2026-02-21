namespace NinePSharp.Interfaces;

using NinePSharp.Constants;

public interface IMessage
{
    uint Size {get;}
    MessageTypes Type {get;}
    ushort Tag {get;}
}