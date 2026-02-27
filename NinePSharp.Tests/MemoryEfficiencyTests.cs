using NinePSharp.Constants;
using System;
using System.Buffers;
using System.Linq;
using System.Text;
using NinePSharp.Messages;
using Xunit;
using FsCheck;
using FsCheck.Xunit;

namespace NinePSharp.Tests
{
    public class MemoryEfficiencyTests
    {
        [Fact]
        public void Twrite_RoundTrip_LargePayload()
        {
            byte[] largeData = new byte[1024 * 1024]; // 1MB
            new Random().NextBytes(largeData);
            
            var twrite = new Twrite(1, 100, 0, largeData);
            
            byte[] buffer = new byte[twrite.Size];
            twrite.WriteTo(buffer);
            
            var parsed = new Twrite(buffer);
            
            Assert.Equal(twrite.Tag, parsed.Tag);
            Assert.Equal(twrite.Fid, parsed.Fid);
            Assert.Equal(twrite.Offset, parsed.Offset);
            Assert.Equal(twrite.Count, parsed.Count);
            Assert.True(largeData.SequenceEqual(parsed.Data.ToArray()));
        }

        [Property]
        public bool Rread_RoundTrip_Property(ushort tag, byte[] data)
        {
            if (data == null) return true;
            
            var rread = new Rread(tag, data);
            byte[] buffer = new byte[rread.Size];
            rread.WriteTo(buffer);
            
            var parsed = new Rread(buffer);
            
            return rread.Tag == parsed.Tag && 
                   data.SequenceEqual(parsed.Data.ToArray());
        }
    }
}
