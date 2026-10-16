namespace NinePSharp.Namespaces;

// namec's #: a name starting with # begins at the device it names, attached with the spec that follows
// the device's rune up to the first slash, and the rest of the name is walked from there.
internal static class NamecPath
{
    internal static bool TryDevice(string path, DeviceAttach? attach, out NamespaceChannel start, out string rest)
    {
        if (attach is null || !path.StartsWith('#'))
        {
            start = null!;
            rest = path;
            return false;
        }

        // The device's rune may itself be a slash, as in #/; the spec runs to the next one.
        string device = path.Length > 1 ? System.Text.Rune.GetRuneAt(path, 1).ToString() : string.Empty;
        int slash = path.IndexOf('/', 1 + device.Length);
        string head = slash < 0 ? path : path[..slash];
        rest = slash < 0 ? string.Empty : path[(slash + 1)..];
        ResourceHandle root = attach(device, head[(1 + device.Length)..]);
        start = NamespaceChannel.Restore([new ChannelFrame(head, root)]);
        return true;
    }
}
