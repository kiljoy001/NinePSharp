using System;
using System.IO;
using SharpFuzz;
using NinePSharp.Backends.Compute;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Messages;

namespace NinePSharp.Backends.Compute.Fuzzer;

public class Program
{
    public static void Main(string[] args)
    {
        var config = new ComputeBackendConfig { MountPath = "/compute" };
        
        SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
        {
            var fs = new ComputeFileSystem(config);
            try
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var data = ms.ToArray();
                var text = System.Text.Encoding.UTF8.GetString(data);
                
                var parts = text.Split(new[] { '/', '\n', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                
                // Stress the 9P state machine
                fs.WalkAsync(new Twalk(1, 1, 2, parts)).Wait();
                fs.OpenAsync(new Topen(1, 2, 0)).Wait();
                fs.WriteAsync(new Twrite(1, 2, 0, data)).Wait();
                fs.ReadAsync(new Tread(1, 2, 0, (uint)data.Length)).Wait();
                fs.StatAsync(new Tstat(1, 2)).Wait();
                fs.ClunkAsync(new Tclunk(1, 2)).Wait();
            }
            catch (Exception)
            {
                // Exceptions are expected for malformed protocol state,
                // but SharpFuzz will catch actual crashes/access violations.
            }
        });
    }
}
