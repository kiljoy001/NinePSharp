using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using LibClient = NinePSharp.Client.NinePClient;

namespace NinePSharp.PowerShell.Internal;

/// <summary>
/// A lightweight 9P client for internal use by PowerShell cmdlets.
/// Now wraps the robust NinePSharp.Client library.
/// </summary>
internal class NinePClient : IDisposable
{
    private readonly LibClient _client;

    public NinePClient(string host, int port)
    {
        _client = new LibClient(host, port);
    }

    public Task VersionAsync() => _client.VersionAsync();

    public async Task<Qid> AttachAsync(uint fid, string user, string export)
    {
        var response = await _client.AttachAsync(fid, NinePConstants.NoFid, user, export);
        return response.Qid;
    }

    public async Task<Qid[]> WalkAsync(uint fid, uint newFid, string[] path)
    {
        var response = await _client.WalkAsync(fid, newFid, path);
        return response.Wqid;
    }

    public Task MkdirAsync(uint dfid, string name, uint mode)
        => _client.CreateAsync(dfid, name, mode | (uint)NinePConstants.FileMode9P.DMDIR, NinePConstants.OREAD);

    public Task WriteAsync(uint fid, ulong offset, byte[] data) => _client.WriteAsync(fid, offset, data);

    public async Task<byte[]> ReadAsync(uint fid, ulong offset, uint count)
    {
        var response = await _client.ReadAsync(fid, offset, count);
        return response.Data.ToArray();
    }

    public Task ClunkAsync(uint fid) => _client.ClunkAsync(fid);

    public Task<byte[]> ReadFileAsync(string path) => _client.ReadFileAsync(path);

    public Task WriteFileAsync(string path, byte[] data) => _client.WriteFileAsync(path, data);

    public void Dispose() => _client.Dispose();
}
