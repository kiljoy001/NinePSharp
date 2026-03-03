using System;
using System.IO;
using NinePSharp.Constants;
using NinePSharp.Parser;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Protocol;
using NinePSharp.Examples;
using System.Threading;
using System.Threading.Tasks;

namespace NinePSharp.Fuzzer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "inmemory")
            {
                FuzzInMemoryHandler();
            }
            else
            {
                FuzzParser();
            }
        }

        private static void FuzzParser()
        {
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        NinePSharp.Parser.NinePParser.parse(NinePDialect.NineP2000U, data.AsMemory());
                    }
                }
                catch (Exception) { }
            });
        }

        private static void FuzzInMemoryHandler()
        {
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var data = ms.ToArray();
                        if (data.Length == 0) return;

                        var fs = new InMemoryHandler();
                        var text = System.Text.Encoding.UTF8.GetString(data);

                        var fileName = text.Split(new[] { '/', '\n', '\r', '\t', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries)
                                           .FirstOrDefault() ?? "fuzzfile.txt";

                        fs.CreateAsync(new string[0], new Tcreate(1, 1, fileName, 0644, 0), CancellationToken.None).Wait();
                        fs.WalkAsync(new string[0], new Twalk(1, 1, 2, new[] { fileName }), CancellationToken.None).Wait();
                        fs.WriteAsync(new string[] { fileName }, new Twrite(1, 2, 0, data), CancellationToken.None).Wait();
                        fs.ReadAsync(new string[] { fileName }, new Tread(1, 2, 0, (uint)Math.Min(data.Length, 8192)), CancellationToken.None).Wait();
                    }
                }
                catch (Exception)
                {
                }
            });
        }
    }
}
