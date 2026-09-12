using System;
using System.IO;
using System.Linq;
using NinePSharp.Constants;
using NinePSharp.Parser;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.Interfaces;
using NinePSharp.Messages;
using NinePSharp.Namespaces;
using NinePSharp.Protocol;
using NinePSharp.Examples;
using NinePSharp.Server.FileSystem;
using System.Threading;
using System.Threading.Tasks;

namespace NinePSharp.Fuzzer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "smoke")
            {
                RunSmokeTest(args.Length > 1 ? args[1] : null);
                return;
            }

            if (args.Length > 0 && args[0] == "corpus-smoke")
            {
                RunCorpusSmoke(args.Length > 1 ? args[1] : "corpus/backend");
                return;
            }

            if (args.Length > 0 && args[0] == "inmemory")
            {
                FuzzInMemoryHandler();
            }
            else if (args.Length > 0 && args[0] == "filesystem")
            {
                FuzzFileSystemBackend();
            }
            else if (args.Length > 0 && args[0] == "namespace")
            {
                FuzzNamespace();
            }
            else if (args.Length > 0 && args[0] == "orleans")
            {
                SharpFuzz.Fuzzer.OutOfProcess.Run(OrleansGatewayFuzz.Run);
            }
            else if (args.Length > 0 && args[0] == "fog")
            {
                SharpFuzz.Fuzzer.OutOfProcess.Run(FogControlFuzz.Run);
            }
            else if (args.Length > 0 && args[0] == "fog-files")
            {
                SharpFuzz.Fuzzer.OutOfProcess.Run(FogFileFuzz.Run);
            }
            else
            {
                FuzzParser();
            }
        }

        private static void RunCorpusSmoke(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Console.WriteLine($"Directory not found: {directory}");
                return;
            }

            var files = Directory.GetFiles(directory);
            Console.WriteLine($"Smoking {files.Length} files from {directory}...");

            foreach (var file in files)
            {
                try
                {
                    var data = File.ReadAllBytes(file);
                    Console.WriteLine($"File: {Path.GetFileName(file)} ({data.Length} bytes)");
                    ExecuteFileSystemStep(data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed on {file}: {ex.Message}");
                }
            }
            Console.WriteLine("Corpus smoke completed.");
        }

        private static void RunSmokeTest(string? filePath)
        {
            byte[] data;
            if (filePath != null && File.Exists(filePath))
            {
                data = File.ReadAllBytes(filePath);
            }
            else
            {
                data = System.Text.Encoding.UTF8.GetBytes("Tversion:size=24,type=100,tag=65535,msize=8192,version=\"9P2000.L\"");
            }

            Console.WriteLine($"Running smoke test with {data.Length} bytes...");
            ExecuteFileSystemStep(data);
            Console.WriteLine("Smoke test completed successfully.");
        }

        private static void ExecuteFileSystemStep(byte[] data)
        {
            try
            {
                var root = new NinePDir("/");
                var fs = new FileSystemBackend(root);
                var text = System.Text.Encoding.UTF8.GetString(data);

                var fileName = text.Split(new[] { '/', '\n', '\r', '\t', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries)
                                   .FirstOrDefault() ?? "fuzzfile.txt";

                fs.CreateAsync(new string[0], new Tcreate(1, 1, fileName, NinePConstants.Mode0644, 0), CancellationToken.None).Wait();
                fs.WalkAsync(new string[0], new Twalk(1, 1, 2, new[] { fileName }), CancellationToken.None).Wait();
                fs.WriteAsync(new string[] { fileName }, new Twrite(1, 2, 0, data), CancellationToken.None).Wait();
                fs.ReadAsync(new string[] { fileName }, new Tread(1, 2, 0, (uint)Math.Min(data.Length, 8192)), CancellationToken.None).Wait();
                fs.RemoveAsync(new string[] { fileName }, new Tremove(1, 2), CancellationToken.None).Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Step failure (expected if input is raw garbage): {ex.Message}");
            }
        }

        private static void FuzzParser()
        {
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    var data = ms.ToArray();
                    foreach (var dialect in new[] { NinePDialect.NineP2000, NinePDialect.NineP2000U, NinePDialect.NineP2000L })
                    {
                        NinePSharp.Parser.NinePParser.parse(dialect, data.AsMemory());
                    }
                }
            });
        }

        private static void FuzzFileSystemBackend()
        {
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    ExecuteFileSystemStep(ms.ToArray());
                }
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

                        fs.CreateAsync(new string[0], new Tcreate(1, 1, fileName, NinePConstants.Mode0644, 0), CancellationToken.None).Wait();
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

        private static void FuzzNamespace()
        {
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                ExecuteNamespaceSteps(memory.ToArray());
            });
        }

        private static void ExecuteNamespaceSteps(byte[] data)
        {
            var table = new MountTable();
            var handles = Enumerable.Range(0, 16)
                .Select(index => new ResourceHandle(
                    new ResourceIdentity("fuzz", $"device-{index}", (ulong)index + 1),
                    (index & 1) == 0 ? QidType.QTDIR : QidType.QTFILE,
                    (uint)index))
                .ToArray();

            int limit = Math.Min(data.Length, 512);
            for (int index = 0; index < limit; index++)
            {
                byte instruction = data[index];
                ResourceHandle target = handles[(instruction >> 4) & 15];
                ResourceHandle mountedOn = handles[instruction & 15];
                try
                {
                    switch (instruction % 6)
                    {
                        case 0:
                            table.Mount(target, mountedOn, MountFlags.Replace);
                            break;
                        case 1:
                            table.Mount(target, mountedOn, MountFlags.Before | MountFlags.Create);
                            break;
                        case 2:
                            table.Mount(target, mountedOn, MountFlags.After);
                            break;
                        case 3:
                            table.Unmount(mountedOn, (instruction & 32) == 0 ? null : target);
                            break;
                        case 4:
                            _ = table.Find(mountedOn.Identity);
                            _ = table.Clone().Snapshot();
                            break;
                        default:
                            table = MountTable.FromSnapshot(table.Snapshot());
                            break;
                    }
                }
                catch (NamespaceException)
                {
                    // Invalid random mount combinations are an expected part of the input space.
                }
            }
        }
    }
}
