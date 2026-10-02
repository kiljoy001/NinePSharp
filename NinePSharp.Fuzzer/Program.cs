using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinePSharp.Constants;
using NinePSharp.Examples;
using NinePSharp.Messages;
using NinePSharp.Namespaces;
using NinePSharp.Parser;
using NinePSharp.Protocol;
using NinePSharp.Server.Configuration.Models;
using NinePSharp.Server.FileSystem;
using NinePSharp.Server.Interfaces;

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
            else if (args.Length > 0 && args[0] == "namespace-syscalls")
            {
                FuzzNamespaceSyscalls();
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
            else if (args.Length > 0 && args[0] == "authorization")
            {
                SharpFuzz.Fuzzer.OutOfProcess.Run(AuthorizationFuzz.Run);
            }
            else if (args.Length > 0 && args[0] == "fog-dispatcher")
            {
                SharpFuzz.Fuzzer.OutOfProcess.Run(FogDispatcherFuzz.Run);
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
                        if (data.Length == 0)
                        {
                            return;
                        }

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

        private static void FuzzNamespaceSyscalls()
        {
            SharpFuzz.Fuzzer.OutOfProcess.Run(stream =>
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                ExecuteNamespaceSyscallSteps(memory.ToArray());
            });
        }

        private static void ExecuteNamespaceSyscallSteps(byte[] data)
        {
            // A missing union fallback used to be accepted as an expected syscall
            // failure. Check a known-valid path as an oracle before random operations.
            if (data.Length > 0)
            {
                CheckUnionTarget(data[0] % 12);
                CheckDescriptorLifetime(data);
                FileSyscallFuzz.Run(data);
                FileCreateFuzz.Run(data);
                DirectoryCursorFuzz.Run(data);
                NativeDirectoryStreamFuzz.Run(data);
                FileStatFuzz.Run(data);
                WStatRecoveryFuzz.Run(data);
            }

            var resources = new SyscallResources();
            ResourceHandle root = resources.Root;
            var mounts = new MountTable();
            var processes = new VProcessTable();
            var process = processes.CreateInitial(
                new NamespaceNavigator(mounts, resources).Attach(root));
            var syscalls = new NamespaceSyscalls(resources);
            string[] paths = { "/source", "/target", "/alternate", "/target/child", "/target/./child", "/target/child/." };
            NamespaceChannel service = new NamespaceNavigator(mounts, resources).Attach(resources.Service);

            for (int index = 0; index + 3 < data.Length && index < 1024; index += 4)
            {
                byte operation = data[index];
                string sourcePath = paths[data[index + 1] % paths.Length];
                string targetPath = paths[data[index + 2] % paths.Length];
                MountFlags flags = (data[index + 3] & 3) switch
                {
                    0 => MountFlags.Replace,
                    1 => MountFlags.Before,
                    2 => MountFlags.After,
                    _ => MountFlags.Cache,
                };

                try
                {
                    switch (operation % 7)
                    {
                        case 0:
                            syscalls.BindAsync(process, sourcePath, targetPath, flags).AsTask().GetAwaiter().GetResult();
                            break;
                        case 1:
                            var source = new NamespaceMountSource(
                                service,
                                (data[index + 1] & 1) == 0 ? NinePConstants.ORDWR : NinePConstants.OREAD,
                                attachName: "fuzz",
                                authenticated: (data[index + 2] & 1) == 0,
                                requiresAuthentication: true);
                            syscalls.MountAsync(process, source, targetPath, flags).AsTask().GetAwaiter().GetResult();
                            break;
                        case 2:
                            syscalls.UnmountAsync(
                                process,
                                targetPath,
                                (data[index + 3] & 4) == 0 ? null : sourcePath).AsTask().GetAwaiter().GetResult();
                            break;
                        case 3:
                            process.ProcessGroup.MountTable.SetMountsDisabled((data[index + 1] & 1) != 0);
                            break;
                        case 4:
                            processes.RforkNamespace(process.Id, (NamespaceForkMode)(data[index + 1] % 3));
                            break;
                        case 5:
                            var group = process.ProcessGroup;
                            var child = processes.Fork(process.Id, NamespaceForkMode.Share);
                            processes.Terminate(process.Id);
                            if (group.MountTable.IsClosed || group.OwnerCount != 1)
                            {
                                throw new InvalidOperationException("Shared child lost namespace ownership.");
                            }

                            process = child;
                            break;
                        default:
                            var released = process.ProcessGroup;
                            processes.Terminate(process.Id);
                            if (!released.MountTable.IsClosed || released.OwnerCount != 0 || released.MountTable.Snapshot().MountHeads.Count != 0)
                            {
                                throw new InvalidOperationException("Last owner did not release its namespace.");
                            }

                            process = processes.CreateInitial(new NamespaceNavigator(mounts, resources).Attach(root));
                            break;
                    }
                }
                catch (NamespaceException)
                {
                    // Invalid syscall combinations are expected in the input space.
                }
            }
        }

        private static void CheckUnionTarget(int prefixCount)
        {
            var resources = new SyscallResources();
            var process = new VProcessTable().CreateInitial(
                new NamespaceNavigator(new MountTable(), resources).Attach(resources.Root));
            MountTable mounts = process.ProcessGroup.MountTable;
            for (int index = 0; index <= prefixCount; index++)
            {
                mounts.Mount(resources.Service, resources.Target, MountFlags.Before);
            }

            var syscalls = new NamespaceSyscalls(resources);
            syscalls.BindAsync(process, "/source", "/target/child/.").AsTask().GetAwaiter().GetResult();
            if (mounts.Find(resources.Child.Identity)?.Mounts[0].Target.Identity != resources.Source.Identity)
            {
                throw new InvalidOperationException("Bind did not reach the later union member.");
            }

            syscalls.UnmountAsync(process, "/target/child", "/source").AsTask().GetAwaiter().GetResult();
            if (mounts.Find(resources.Child.Identity) is not null)
            {
                throw new InvalidOperationException("Unmount did not release the union child mount.");
            }
        }

        private static void CheckDescriptorLifetime(byte[] data)
        {
            var resources = new SyscallResources();
            var processes = new VProcessTable();
            var parent = processes.CreateInitial(new NamespaceNavigator(new MountTable(), resources).Attach(resources.Root));
            int closes = 0;
            parent.Descriptors.Install(new ResourceOpenHandle(resources.Child, "fuzz-open", NinePConstants.ORDWR, 0), () =>
            {
                Interlocked.Increment(ref closes);
                return ValueTask.CompletedTask;
            });

            // This admitted operation is an independent, known-live reference.
            var lease = parent.Descriptors.Acquire(0);
            foreach (byte operation in data.Take(16))
            {
                DescriptorForkMode mode = (DescriptorForkMode)(operation % 3);
                var child = processes.Fork(parent.Id, (NamespaceForkMode)((operation / 3) % 3), descriptorMode: mode);
                if (child.Descriptors.Snapshot().Count != (mode == DescriptorForkMode.Empty ? 0 : 1))
                {
                    throw new InvalidOperationException("Descriptor inheritance did not match rfork mode.");
                }

                if (mode != DescriptorForkMode.Empty)
                {
                    int duplicate = child.Descriptors.DuplicateAsync(0).AsTask().GetAwaiter().GetResult();
                    child.Descriptors.CloseAsync(duplicate).AsTask().GetAwaiter().GetResult();
                }

                processes.TerminateAsync(child.Id).GetAwaiter().GetResult();
                if (parent.Descriptors.OwnerCount != 1 || closes != 0)
                {
                    throw new InvalidOperationException("Child exit released a surviving descriptor owner.");
                }
            }

            var group = parent.Descriptors;
            processes.TerminateAsync(parent.Id).GetAwaiter().GetResult();
            if (!group.IsClosed || group.OwnerCount != 0 || group.Snapshot().Count != 0 || closes != 0)
            {
                throw new InvalidOperationException("Process exit lost the admitted I/O reference.");
            }

            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (closes != 1)
            {
                throw new InvalidOperationException("Final channel reference did not close exactly once.");
            }
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

        private sealed class SyscallResources : IResourceOperations
        {
            private readonly Dictionary<ResourceIdentity, Dictionary<string, ResourceHandle>> children = new();
            private ulong nextPath;

            internal SyscallResources()
            {
                Root = Add("root", true);
                Source = AddChild(Root, "source", true);
                Target = AddChild(Root, "target", true);
                Child = AddChild(Target, "child", true);
                _ = AddChild(Root, "alternate", true);
                Service = Add("service", true);
            }

            internal ResourceHandle Root { get; }

            internal ResourceHandle Source { get; }

            internal ResourceHandle Target { get; }

            internal ResourceHandle Child { get; }

            internal ResourceHandle Service { get; }

            public ValueTask<ResourceHandle?> WalkAsync(
                ResourceHandle directory,
                string name,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(
                    children.TryGetValue(directory.Identity, out Dictionary<string, ResourceHandle>? entries) &&
                    entries.TryGetValue(name, out ResourceHandle? child)
                        ? child
                        : null);
            }

            public ValueTask<IReadOnlyList<ResourceDirectoryEntry>> ReadDirectoryAsync(
                ResourceHandle directory,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!children.TryGetValue(directory.Identity, out Dictionary<string, ResourceHandle>? entries))
                {
                    return ValueTask.FromResult<IReadOnlyList<ResourceDirectoryEntry>>(Array.Empty<ResourceDirectoryEntry>());
                }

                return ValueTask.FromResult<IReadOnlyList<ResourceDirectoryEntry>>(
                    entries.Select(pair => new ResourceDirectoryEntry(pair.Key, pair.Value)).ToArray());
            }

            public ValueTask<ResourceHandle> CreateAsync(
                ResourceHandle directory,
                string name,
                bool directoryEntry,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(AddChild(directory, name, directoryEntry));
            }

            private ResourceHandle Add(string device, bool directory)
            {
                var handle = new ResourceHandle(
                    new ResourceIdentity("fuzzer", device, ++nextPath),
                    directory ? QidType.QTDIR : QidType.QTFILE);
                children.Add(handle.Identity, new Dictionary<string, ResourceHandle>(StringComparer.Ordinal));
                return handle;
            }

            private ResourceHandle AddChild(ResourceHandle parent, string name, bool directory)
            {
                ResourceHandle child = Add(parent.Identity.Device + "/" + name, directory);
                children[parent.Identity][name] = child;
                return child;
            }
        }
    }
}
