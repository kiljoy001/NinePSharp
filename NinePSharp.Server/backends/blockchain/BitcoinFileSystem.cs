using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.RPC;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Server.Backends;

public class BitcoinFileSystem : INinePFileSystem
{
    private readonly BitcoinBackendConfig _config;
    private readonly RPCClient? _rpcClient;
    private readonly ILuxVaultService _vault;
    private List<string> _currentPath = new();

    public BitcoinFileSystem(BitcoinBackendConfig config, RPCClient? rpcClient, ILuxVaultService vault)
    {
        _config = config;
        _rpcClient = rpcClient;
        _vault = vault;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        var qids = new List<Qid>();
        foreach (var name in twalk.Wname)
        {
            if (name == "..")
            {
                if (_currentPath.Count > 0) _currentPath.RemoveAt(_currentPath.Count - 1);
            }
            else
            {
                _currentPath.Add(name);
            }
            qids.Add(new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode()));
        }
        return new Rwalk(twalk.Tag, qids.ToArray());
    }

    public async Task<Ropen> OpenAsync(Topen topen) => new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 0), 0);

    public async Task<Rread> ReadAsync(Tread tread) => new Rread(tread.Tag, Array.Empty<byte>());

    public async Task<Rwrite> WriteAsync(Twrite twrite) => throw new NotSupportedException();
    
    public async Task<Rclunk> ClunkAsync(Tclunk tclunk) => new Rclunk(tclunk.Tag);

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        var name = _currentPath.Count > 0 ? _currentPath.Last() : "bitcoin";
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, 0), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, name, "scott", "scott", "scott");
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rwstat> WstatAsync(Twstat twstat) => throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new BitcoinFileSystem(_config, _rpcClient, _vault);
        clone._currentPath = new List<string>(_currentPath);
        return clone;
    }
}
