using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Client;

public class RemoteFs
{
    private readonly NinePClient _client;
    private readonly uint _rootFid;

    internal RemoteFs(NinePClient client, uint rootFid)
    {
        _client = client;
        _rootFid = rootFid;
    }

    public async Task<byte[]> ReadFileAsync(string path)
    {
        uint fileFid = _client.GetNextFid();
        string[] components = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        await _client.WalkAsync(_rootFid, fileFid, components);
        await _client.OpenAsync(fileFid, NinePConstants.OREAD);
        
        var result = new List<byte>();
        ulong offset = 0;
        while (true)
        {
            var rread = await _client.ReadAsync(fileFid, offset, _client.MSize - 24);
            if (rread.Count == 0) break;
            result.AddRange(rread.Data.ToArray());
            offset += rread.Count;
        }

        await _client.ClunkAsync(fileFid);
        return result.ToArray();
    }

    public async Task<Stat> StatAsync(string path)
    {
        uint fileFid = _client.GetNextFid();
        string[] components = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        await _client.WalkAsync(_rootFid, fileFid, components);
        var rstat = await _client.StatAsync(fileFid);
        await _client.ClunkAsync(fileFid);
        
        return rstat.Stat;
    }

    public async Task WriteFileAsync(string path, byte[] data)
    {
        // Reuse NinePClient.WriteFileAsync logic but with this rootFid
        // (For brevity, I'll implement it here properly)
        uint dirFid = _client.GetNextFid();
        string[] components = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string fileName = components.Last();
        string[] dirPath = components.Take(components.Length - 1).ToArray();

        await _client.WalkAsync(_rootFid, dirFid, dirPath);

        try
        {
            uint fileFid = _client.GetNextFid();
            var rwalk = await _client.WalkAsync(dirFid, fileFid, new[] { fileName });
            if (rwalk.Wqid.Length == 1)
            {
                await _client.ClunkAsync(dirFid);
                dirFid = fileFid;
                await _client.OpenAsync(dirFid, (byte)(NinePConstants.OWRITE | 0x10));
            }
            else
            {
                await _client.ClunkAsync(fileFid);
                await _client.CreateAsync(dirFid, fileName, 0644, NinePConstants.OWRITE);
            }
        }
        catch
        {
            await _client.CreateAsync(dirFid, fileName, 0644, NinePConstants.OWRITE);
        }

        uint maxWrite = _client.MSize - 32;
        int sent = 0;
        while (sent < data.Length)
        {
            int toSend = Math.Min(data.Length - sent, (int)maxWrite);
            byte[] chunk = new byte[toSend];
            Array.Copy(data, sent, chunk, 0, toSend);
            await _client.WriteAsync(dirFid, (ulong)sent, chunk);
            sent += toSend;
        }

        await _client.ClunkAsync(dirFid);
    }
}
