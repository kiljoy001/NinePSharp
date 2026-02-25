using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using NinePSharp.Messages;
using NinePSharp.Server.Cluster.Actors;
using NinePSharp.Server.Cluster.Messages;
using NinePSharp.Server.Interfaces;
using Moq;
using Xunit;

namespace NinePSharp.Tests;

public class ClusterChaosTests : TestKit
{
    [Fact]
    public void SessionActor_Handles_Simultaneous_Writes_Without_Deadlock()
    {
        // Arrange
        var mockFs = new Mock<INinePFileSystem>();
        mockFs.Setup(f => f.WriteAsync(It.IsAny<Twrite>()))
              .Returns(async (Twrite t) => {
                  await Task.Delay(10); // Simulate some work
                  return new Rwrite(t.Tag, (uint)t.Data.Length);
              });

        var actor = Sys.ActorOf(Props.Create(() => new NinePSessionActor(mockFs.Object)));

        // Act - Fire 50 simultaneous writes
        for (ushort i = 0; i < 50; i++)
        {
            var dto = new TWriteDto { Tag = i, Fid = 1, Data = new byte[] { 1, 2, 3 } };
            actor.Tell(dto);
        }

        // Assert - Expect 50 responses
        for (int i = 0; i < 50; i++)
        {
            ExpectMsg<RWriteDto>(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void SessionActor_Propagates_Errors_Correctly()
    {
        // Arrange
        var mockFs = new Mock<INinePFileSystem>();
        mockFs.Setup(f => f.ReadAsync(It.IsAny<Tread>()))
              .ThrowsAsync(new Exception("Remote drive failed"));

        var actor = Sys.ActorOf(Props.Create(() => new NinePSessionActor(mockFs.Object)));

        // Act
        var dto = new TReadDto { Tag = 1, Fid = 1, Count = 1024 };
        actor.Tell(dto);

        // Assert
        var error = ExpectMsg<RErrorDto>();
        Assert.Equal("Remote drive failed", error.Ename);
    }
}
