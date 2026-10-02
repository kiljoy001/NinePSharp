using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;

namespace NinePSharp.Tests.Helpers;

internal static class DispatcherIntegrationTestKit
{
    internal static NinePFSDispatcher CreateDispatcher(INinePRequestHandler handler)
    {
        return new NinePFSDispatcher(
            NullLogger<NinePFSDispatcher>.Instance,
            handler);
    }

    internal static async Task AttachRootAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid)
        => await AttachAsync(dispatcher, tag, fid, "/");

    internal static async Task AttachAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, string aname)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTattach(new Tattach(tag, fid, NinePConstants.NoFid, "user", aname)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rattach)
        {
            string errMsg = response is Rerror err ? err.Ename : string.Empty;
            throw new Xunit.Sdk.XunitException($"Expected Rattach, got {response.GetType().Name} ({errMsg})");
        }
    }

    internal static async Task<Rwalk> WalkAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, uint newFid, string[] wname)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTwalk(new Twalk(tag, fid, newFid, wname)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rwalk walk)
        {
            string errMsg = response is Rerror err ? err.Ename : string.Empty;
            throw new Xunit.Sdk.XunitException($"Expected Rwalk, got {response.GetType().Name} ({errMsg})");
        }

        return walk;
    }

    internal static async Task<Rread> ReadAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, ulong offset, uint count)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTread(new Tread(tag, fid, offset, count)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rread read)
        {
            string errMsg = response is Rerror err ? err.Ename : string.Empty;
            throw new Xunit.Sdk.XunitException($"Expected Rread, got {response.GetType().Name} ({errMsg})");
        }

        return read;
    }

    internal static async Task<Rreaddir> ReaddirAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, ulong offset, uint count)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTreaddir(new Treaddir(24, tag, fid, offset, count)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rreaddir readdir)
        {
            var errMsg = response is Rerror err ? $": {err.Ename}" : string.Empty;
            throw new Xunit.Sdk.XunitException($"Expected Rreaddir, got {response.GetType().Name}{errMsg}");
        }

        return readdir;
    }

    internal static async Task<Rwrite> WriteAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, ulong offset, byte[] data)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTwrite(new Twrite(tag, fid, offset, data)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rwrite write)
        {
            string errMsg = response is Rerror err ? err.Ename : string.Empty;
            throw new Xunit.Sdk.XunitException($"Expected Rwrite, got {response.GetType().Name} ({errMsg})");
        }

        return write;
    }

    internal static async Task<Ropen> OpenAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, byte mode = 0)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTopen(new Topen(tag, fid, mode)),
            dialect: NinePDialect.NineP2000);

        if (response is not Ropen open)
        {
            string errMsg = response is Rerror err ? err.Ename : string.Empty;
            throw new Xunit.Sdk.XunitException($"Expected Ropen, got {response.GetType().Name} ({errMsg})");
        }

        return open;
    }

    internal static async Task<Rcreate> CreateAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid, string name, uint perm = NinePConstants.Mode0644, byte mode = 0)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTcreate(new Tcreate(tag, fid, name, perm, mode)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rcreate create)
        {
            string errMsg = response is Rerror err ? err.Ename : string.Empty;
            throw new Xunit.Sdk.XunitException($"Expected Rcreate, got {response.GetType().Name} ({errMsg})");
        }

        return create;
    }

    internal static async Task<Rstat> StatAsync(NinePFSDispatcher dispatcher, ushort tag, uint fid)
    {
        var response = await dispatcher.DispatchAsync(
            "test-session",
            NinePMessage.NewMsgTstat(new Tstat(tag, fid)),
            dialect: NinePDialect.NineP2000);

        if (response is not Rstat stat)
        {
            string errMsg = response is Rerror err ? err.Ename : string.Empty;
            throw new Xunit.Sdk.XunitException($"Expected Rstat, got {response.GetType().Name} ({errMsg})");
        }

        return stat;
    }

    internal static string ReadPayload(Rread read) => Encoding.UTF8.GetString(read.Data.Span);

    internal static List<Stat> ParseStatsTable(ReadOnlySpan<byte> data)
    {
        var result = new List<Stat>();
        int offset = 0;

        while (offset < data.Length)
        {
            result.Add(new Stat(data, ref offset));
        }

        return result;
    }

    internal static string CleanMount(string? raw, int index)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return $"m{index}";
        }

        var chars = raw
            .Where(c => char.IsLetterOrDigit(c) || c is '_' or '-')
            .Take(24)
            .ToArray();

        return chars.Length == 0 ? $"m{index}" : new string(chars);
    }

    internal readonly record struct ReaddirEntry(QidType QidType, ulong NextOffset, string Name);
}
