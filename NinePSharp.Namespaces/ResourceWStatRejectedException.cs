using System.Security.Cryptography;
using System.Text;

namespace NinePSharp.Namespaces;

/// <summary>A definite provider rejection which guarantees that the requested mutation was not applied.</summary>
public sealed class ResourceWStatRejectedException(string message) : IOException(message);
