using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Messages;

namespace NinePSharp.Client;

public class RemoteFs
{
    private readonly NinePClient client;
    private readonly uint rootFid;

    internal RemoteFs(NinePClient client, uint rootFid)
    {
        this.client = client;
        this.rootFid = rootFid;
    }

    public async Task<byte[]> ReadFileAsync(string path)
    {
        uint fileFid = client.GetNextFid();
        string[] components = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        await client.WalkAsync(rootFid, fileFid, components);
        await client.OpenAsync(fileFid, NinePConstants.OREAD);

        var result = new List<byte>();
        ulong offset = 0;
        while (true)
        {
            var rread = await client.ReadAsync(fileFid, offset, client.MSize - 24);
            if (rread.Count == 0)
            {
                break;
            }

            result.AddRange(rread.Data.ToArray());
            offset += rread.Count;
        }

        await client.ClunkAsync(fileFid);
        return result.ToArray();
    }

    public async Task<Stat> StatAsync(string path)
    {
        uint fileFid = client.GetNextFid();
        string[] components = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        await client.WalkAsync(rootFid, fileFid, components);
        var rstat = await client.StatAsync(fileFid);
        await client.ClunkAsync(fileFid);

        return rstat.Stat;
    }

    public async Task WriteFileAsync(string path, byte[] data)
    {
        // Reuse NinePClient.WriteFileAsync logic but with this rootFid
        // (For brevity, I'll implement it here properly)
        uint dirFid = client.GetNextFid();
        string[] components = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string fileName = components.Last();
        string[] dirPath = components.Take(components.Length - 1).ToArray();

        await client.WalkAsync(rootFid, dirFid, dirPath);

        try
        {
            uint fileFid = client.GetNextFid();
            var rwalk = await client.WalkAsync(dirFid, fileFid, new[] { fileName });
            if (rwalk.Wqid.Length == 1)
            {
                await client.ClunkAsync(dirFid);
                dirFid = fileFid;
                await client.OpenAsync(dirFid, (byte)(NinePConstants.OWRITE | 0x10));
            }
            else
            {
                await client.ClunkAsync(fileFid);
                await client.CreateAsync(dirFid, fileName, NinePConstants.Mode0644, NinePConstants.OWRITE);
            }
        }
        catch
        {
            await client.CreateAsync(dirFid, fileName, NinePConstants.Mode0644, NinePConstants.OWRITE);
        }

        uint maxWrite = client.MSize - 32;
        int sent = 0;
        while (sent < data.Length)
        {
            int toSend = Math.Min(data.Length - sent, (int)maxWrite);
            byte[] chunk = new byte[toSend];
            Array.Copy(data, sent, chunk, 0, toSend);
            await client.WriteAsync(dirFid, (ulong)sent, chunk);
            sent += toSend;
        }

        await client.ClunkAsync(dirFid);
    }
}
