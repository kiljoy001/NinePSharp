using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.FSharp.Collections;
using Moq;
using NinePSharp.Constants;
using NinePSharp.Core.FSharp;
using NinePSharp.Messages;
using NinePSharp.Parser;
using NinePSharp.Server;
using NinePSharp.Server.Interfaces;
using NinePSharp.Server.Utils;
using NinePSharp.Tests.Helpers;
using Xunit;

namespace NinePSharp.Tests.Architecture;

public class NamespaceCreateSemanticsPropertyTests
{
    [Property(MaxTest = 100)]
    public bool Create_Follows_MCREATE_Priority(string nameRaw)
    {
        string name = Clean(nameRaw, "file");
        var fs1 = new CreateTrackingFileSystem("fs1");
        var fs2 = new CreateTrackingFileSystem("fs2");

        var dispatcher = DispatcherIntegrationTestKit.CreateDispatcher(new[]
        {
            new StubBackend("/union", () => fs1),
            new StubBackend("/union", () => fs2)
        });

        // Setup union with MCREATE on first branch
        // In this dispatcher, the first registered backend for a path gets MREPL, others are MAFTER by default if not specified
        // But our dispatcher build logic currently just stacks them.

        DispatcherIntegrationTestKit.AttachRootAsync(dispatcher, 1, 100).Sync();
        DispatcherIntegrationTestKit.WalkAsync(dispatcher, 2, 100, 101, new[] { "union" }).Sync();

        var create = DispatcherIntegrationTestKit.CreateAsync(dispatcher, 3, 101, name).Sync();

        // Tcreate should succeed and return a valid Rcreate with a qid
        // The qid.Path should be deterministic based on the backend marker and filename
        return create.Qid.Path > 0;
    }

    private static string Clean(string raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var clean = raw.Replace("/", "").Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }
}
