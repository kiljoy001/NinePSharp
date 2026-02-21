using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Solnet.Rpc;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class SolanaFileSystem : INinePFileSystem
{
    private readonly SolanaBackendConfig _config;
    private readonly IRpcClient? _rpcClient;
    private readonly ILuxVaultService _vault;

    public SolanaFileSystem(SolanaBackendConfig config, IRpcClient? rpcClient, ILuxVaultService vault)
    {
        _config = config;
        _rpcClient = rpcClient;
        _vault = vault;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());
        var qids = new List<Qid>();
        foreach (var name in twalk.Wname)
        {
            qids.Add(new Qid(QidType.QTFILE, 0, (ulong)name.GetHashCode()));
        }
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public async Task<Ropen> OpenAsync(Topen topen)
    {
        return new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 0), 0);
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (tread.Offset == 0)
        {
            var result = $"RPC: {_config.RpcUrl}\n";
            if (_rpcClient != null)
            {
                try 
                {
                    var version = await _rpcClient.GetVersionAsync();
                    result += $"Version: {version.Result.SolanaCore}\n";
                    var supply = await _rpcClient.GetSupplyAsync();
                    result += $"Total Supply: {supply.Result.Value.Total / 1000000000.0} SOL\n";
                }
                catch (Exception ex)
                {
                    result += $"RPC Error: {ex.Message}\n";
                }
            }
            return new Rread(tread.Tag, System.Text.Encoding.UTF8.GetBytes(result));
        }
        return new Rread(tread.Tag, Array.Empty<byte>());
    }

    public async Task<Rwrite> WriteAsync(Twrite twrite) => throw new NotSupportedException();
    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);
    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, 0), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, "sol", "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }
    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        return new SolanaFileSystem(_config, _rpcClient, _vault);
    }
}
