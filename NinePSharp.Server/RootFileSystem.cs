using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Akka.Actor;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Constants;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Cluster;
using NinePSharp.Server.Cluster.Actors;

namespace NinePSharp.Server;

internal class RootFileSystem : INinePFileSystem
{
    private readonly List<IProtocolBackend> _backends;
    private readonly IClusterManager? _clusterManager;
    private readonly TimeSpan _clusterTimeout = TimeSpan.FromSeconds(3);
    private INinePFileSystem? _delegatedFs;

    public bool DotU { get; set; }

    public RootFileSystem(List<IProtocolBackend> backends, IClusterManager? clusterManager = null)
    {
        _backends = backends;
        _clusterManager = clusterManager;
    }

    public async Task<Rwalk> WalkAsync(Twalk twalk)
    {
        // If we are already delegating, pass the walk to the backend
        if (_delegatedFs != null) return await _delegatedFs.WalkAsync(twalk);

        if (twalk.Wname.Length == 0) return new Rwalk(twalk.Tag, Array.Empty<Qid>());

        // Try to find a backend that matches the first element of the walk
        var name = twalk.Wname[0];
        var backend = _backends.FirstOrDefault(b => b.MountPath.Trim('/') == name);

        if (backend == null && _clusterManager?.Registry != null)
        {
            var mountPath = "/" + name;
            var registryResponse = await _clusterManager.Registry.Ask<object>(new GetBackend(mountPath), _clusterTimeout);
            if (registryResponse is BackendFound found)
            {
                var sessionResponse = await found.Actor.Ask<object>(new SpawnSession(), _clusterTimeout);
                if (sessionResponse is SessionSpawned session)
                {
                    _delegatedFs = new RemoteFileSystem(session.Session) { DotU = DotU };

                    var qid = new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode());
                    var qids = new List<Qid> { qid };

                    if (twalk.Wname.Length > 1)
                    {
                        var subWalk = new Twalk(twalk.Tag, twalk.Fid, twalk.NewFid, twalk.Wname.Skip(1).ToArray());
                        var subResponse = await _delegatedFs.WalkAsync(subWalk);
                        if (subResponse.Wqid != null)
                        {
                            qids.AddRange(subResponse.Wqid);
                        }
                    }

                    return new Rwalk(twalk.Tag, qids.ToArray());
                }
            }
        }

        if (backend != null)
        {
            // Enter the backend
            _delegatedFs = backend.GetFileSystem(null);
            _delegatedFs.DotU = this.DotU;

            var qid = new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode());
            var qids = new List<Qid> { qid };

            if (twalk.Wname.Length > 1)
            {
                // We have more steps! 
                // We must create a TWALK that starts from the backend root.
                // NOTE: the FID in twalk is the root fid, but the sub-walk
                // is conceptually walking relative to the new delegated FS.
                var subWalk = new Twalk(twalk.Tag, twalk.Fid, twalk.NewFid, twalk.Wname.Skip(1).ToArray());
                var subResponse = await _delegatedFs.WalkAsync(subWalk);
                
                if (subResponse.Wqid != null)
                {
                    qids.AddRange(subResponse.Wqid);
                }
            }

            return new Rwalk(twalk.Tag, qids.ToArray());
        }

        return new Rwalk(twalk.Tag, null); 
    }

    public async Task<Rread> ReadAsync(Tread tread)
    {
        if (_delegatedFs != null) return await _delegatedFs.ReadAsync(tread);

        var mountNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var backend in _backends)
        {
            var name = backend.MountPath.Trim('/');
            if (string.IsNullOrEmpty(name)) continue;
            mountNames.Add(name);
        }

        if (_clusterManager?.Registry != null)
        {
            try
            {
                var response = await _clusterManager.Registry.Ask<object>(new GetBackends(), _clusterTimeout);
                if (response is BackendsSnapshot snapshot)
                {
                    foreach (var mountPath in snapshot.MountPaths)
                    {
                        var name = mountPath.Trim('/');
                        if (string.IsNullOrEmpty(name)) continue;
                        mountNames.Add(name);
                    }
                }
            }
            catch
            {
                // Best-effort federation listing; root should still serve local mounts.
            }
        }

        var entries = new List<byte>();
        foreach (var name in mountNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, (ulong)name.GetHashCode()), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, name, "scott", "scott", "scott", dotu: DotU);

            var entryBuffer = new byte[stat.Size];
            int offset = 0;
            stat.WriteTo(entryBuffer, ref offset);
            entries.AddRange(entryBuffer);
        }

        var allData = entries.ToArray();
        if (tread.Offset >= (ulong)allData.Length) return new Rread(tread.Tag, Array.Empty<byte>());

        int totalToSend = 0;
        int currentOffset = (int)tread.Offset;
        while (currentOffset + 2 <= allData.Length)
        {
            int entrySize = BinaryPrimitives.ReadUInt16LittleEndian(allData.AsSpan(currentOffset, 2)) + 2;
            if (entrySize <= 0 || currentOffset + entrySize > allData.Length) break;
            if (totalToSend + entrySize > tread.Count) break;
            totalToSend += entrySize;
            currentOffset += entrySize;
        }

        if (totalToSend == 0) return new Rread(tread.Tag, Array.Empty<byte>());
        return new Rread(tread.Tag, allData.AsMemory((int)tread.Offset, totalToSend).ToArray());
    }

    public async Task<Rstat> StatAsync(Tstat tstat)
    {
        if (_delegatedFs != null) return await _delegatedFs.StatAsync(tstat);
        var stat = new Stat(0, 0, 0, new Qid(QidType.QTDIR, 0, 0), 0755 | (uint)NinePConstants.FileMode9P.DMDIR, 0, 0, 0, "/", "scott", "scott", "scott", dotu: DotU);
        return new Rstat(tstat.Tag, stat);
    }

    public Task<Rclunk> ClunkAsync(Tclunk tclunk) => _delegatedFs?.ClunkAsync(tclunk) ?? Task.FromResult(new Rclunk(tclunk.Tag));
    
    public async Task<Ropen> OpenAsync(Topen topen)
    {
        if (_delegatedFs != null) return await _delegatedFs.OpenAsync(topen);
        return new Ropen(topen.Tag, new Qid(QidType.QTDIR, 0, 0), 0);
    }

    public Task<Rwrite> WriteAsync(Twrite twrite) => _delegatedFs?.WriteAsync(twrite) ?? throw new NotSupportedException();
    public Task<Rwstat> WstatAsync(Twstat twstat) => _delegatedFs?.WstatAsync(twstat) ?? throw new NotSupportedException();
    public Task<Rremove> RemoveAsync(Tremove tremove) => _delegatedFs?.RemoveAsync(tremove) ?? throw new NotSupportedException();

    public Task<Rgetattr> GetAttrAsync(Tgetattr tgetattr)
    {
        if (_delegatedFs != null) return _delegatedFs.GetAttrAsync(tgetattr);
        
        var qid = new Qid(QidType.QTDIR, 0, 0);
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Task.FromResult(new NinePSharp.Messages.Rgetattr(tgetattr.Tag, (ulong)NinePConstants.GetAttrMask.P9_GETATTR_BASIC, qid, (uint)NinePConstants.FileMode9P.DMDIR | 0x1EDu, 0, 0, 1, 0, 0, 4096, 0, now, 0, now, 0, now, 0, 0, 0, 0, 0));
    }

    public Task<Rsetattr> SetAttrAsync(Tsetattr tsetattr) => _delegatedFs?.SetAttrAsync(tsetattr) ?? throw new NotSupportedException();

    public INinePFileSystem Clone()
    {
        var clone = new RootFileSystem(_backends, _clusterManager) { DotU = this.DotU };
        if (_delegatedFs != null) clone._delegatedFs = _delegatedFs.Clone();
        return clone;
    }
}
