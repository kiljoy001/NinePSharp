using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Server;
using Xunit;

namespace NinePSharp.Tests;

public sealed class RemoteMountProviderContractPropertyFuzzTests
{
    [Property(MaxTest = 40)]
    public bool NullRemoteMountProvider_Is_Safe_For_Unknown_And_Registered_Mounts(NonEmptyString rawPath)
    {
        string path = NormalizeMountPath(rawPath.Get);
        using IRemoteMountProvider sut = new NullRemoteMountProvider();

        sut.Start();
        sut.RegisterMountAsync(path, () => RuntimeFileSystemAdapter.ToRuntime(new TaggedFileSystem("ignored"))).Sync();

        var mounts = sut.GetRemoteMountPathsAsync().Sync();
        var resolved = sut.TryCreateRemoteRuntimeAsync(path).Sync();
        sut.StopAsync().Sync();

        return mounts.Count == 0 && resolved == null;
    }

    private static string NormalizeMountPath(string raw)
    {
        var chars = raw.Where(c => char.IsLetterOrDigit(c) || c == '/' || c == '_' || c == '-')
            .Take(20)
            .ToArray();
        string cleaned = new string(chars).Trim('/');
        return string.IsNullOrWhiteSpace(cleaned) ? "/mount" : "/" + cleaned;
    }

    private sealed class TaggedFileSystem : INinePFileSystem
    {
        private readonly byte[] _payload;

        public TaggedFileSystem(string tag)
        {
            _payload = Encoding.UTF8.GetBytes(tag);
        }

        public NinePDialect Dialect { get; set; } = NinePDialect.NineP2000;

        public Task<Rwalk> WalkAsync(Twalk twalk) => Task.FromResult(new Rwalk(twalk.Tag, Array.Empty<Qid>()));
        public Task<Ropen> OpenAsync(Topen topen) => Task.FromResult(new Ropen(topen.Tag, new Qid(QidType.QTFILE, 0, 1), 0));
        public Task<Rread> ReadAsync(Tread tread) => Task.FromResult(new Rread(tread.Tag, _payload));
        public Task<Rwrite> WriteAsync(Twrite twrite) => Task.FromResult(new Rwrite(twrite.Tag, twrite.Count));
        public Task<Rclunk> ClunkAsync(Tclunk tclunk) => Task.FromResult(new Rclunk(tclunk.Tag));
        public Task<Rstat> StatAsync(Tstat tstat) => Task.FromResult(new Rstat(tstat.Tag, new Stat(0, 0, 0, new Qid(QidType.QTFILE, 0, 1), 0, 0, 0, 0, "tag", "none", "none", "none")));
        public Task<Rwstat> WstatAsync(Twstat twstat) => Task.FromResult(new Rwstat(twstat.Tag));
        public Task<Rremove> RemoveAsync(Tremove tremove) => Task.FromResult(new Rremove(tremove.Tag));
        public Task<Rcreate> CreateAsync(Tcreate tcreate) => Task.FromResult(new Rcreate(tcreate.Tag, new Qid(QidType.QTFILE, 0, 1), 0));
        public INinePFileSystem Clone() => new TaggedFileSystem(Encoding.UTF8.GetString(_payload)) { Dialect = Dialect };
    }

    private class NullRemoteMountProvider : IRemoteMountProvider
    {
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public Task RegisterMountAsync(string mountPath, Func<IBackendRuntime> createRuntime) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetRemoteMountPathsAsync() => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IBackendRuntime?> TryCreateRemoteRuntimeAsync(string mountPath) => Task.FromResult<IBackendRuntime?>(null);
        public void Dispose() { }
    }
}
