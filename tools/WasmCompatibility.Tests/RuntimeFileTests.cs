using System.Runtime.InteropServices;
using NinePSharp.Namespaces;
using NinePSharp.Namespaces.Tests.Support;
using WebAssembly;
using WebAssembly.Instructions;
using WebAssembly.Runtime;
using Xunit;
using FunctionImport = WebAssembly.Runtime.FunctionImport;

namespace WasmCompatibility.Tests;

// An integration fixture, NOT the production WASI adapter. It deliberately supplies
// one already-open regular file and one iovec per call. Full capabilities, shared
// offsets, path_open, error translation and process supervision are specified separately.
public sealed class RuntimeFileTests
{
    [Fact]
    public void ImportsRequireNumericResultsRatherThanTasks()
    {
        Assert.Throws<ArgumentException>(() => new FunctionImport(new Func<Task<int>>(() => Task.FromResult(0))));
        Assert.Throws<ArgumentException>(() => new FunctionImport(new Func<ValueTask<int>>(() => ValueTask.FromResult(0))));
    }

    [Fact]
    public async Task CompiledGuestWritesAndPositionedReadsANamespaceFileAfterMemoryGrowth()
    {
        await using var host = await FileHost.CreateAsync();
        using var instance = CreateModule().Compile<Guest>()(host.Imports);
        host.Memory = instance.Exports.memory;
        Assert.Equal(1u, host.Memory.Grow(1));
        int buffer = 65536; // Access the new page through the current memory base.
        byte[] input = [0, 255, 10, 42, 128];
        Marshal.Copy(input, 0, host.Memory.Start + buffer, input.Length);
        host.SetVector(buffer, input.Length);
        Assert.Equal(0, await OnWorker(() => instance.Exports.Write(host.Fd, 0, 1, 16)));
        Assert.Equal(input.Length, Marshal.ReadInt32(host.Memory.Start + 16));
        host.SetVector(buffer + 32, 20);
        Assert.Equal(0, await OnWorker(() => instance.Exports.Read(host.Fd, 0, 1, 1, 16)));
        Assert.Equal(4, Marshal.ReadInt32(host.Memory.Start + 16)); // short read at EOF
        byte[] actual = new byte[4];
        Marshal.Copy(host.Memory.Start + buffer + 32, actual, 0, actual.Length);
        Assert.Equal(input[1..], actual);
        Assert.Equal(0, await OnWorker(() => instance.Exports.Read(host.Fd, 0, 1, 5, 16)));
        Assert.Equal(0, Marshal.ReadInt32(host.Memory.Start + 16)); // EOF
        Assert.Equal(0, await OnWorker(() => instance.Exports.Close(host.Fd)));
        Assert.Equal(1, host.Resources.ClunkCount);
        Assert.Equal(8, await OnWorker(() => instance.Exports.Close(host.Fd))); // BADF
    }

    [Fact]
    public async Task PendingImportRetainsHandleWhileDescriptorCloses()
    {
        await using var host = await FileHost.CreateAsync();
        using var instance = CreateModule().Compile<Guest>()(host.Imports);
        host.Memory = instance.Exports.memory;
        host.SetVector(128, 1);
        Marshal.WriteByte(host.Memory.Start + 128, 73);
        host.PauseWrite = true;
        Task<int> guest = OnWorker(() => instance.Exports.Write(host.Fd, 0, 1, 16));
        try
        {
            await host.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(guest.IsCompleted);
            await host.Descriptors.CloseAsync(host.Fd); // coordinator remains responsive
            Assert.Empty(host.Descriptors.Snapshot());
            Assert.Equal(0, host.Resources.ClunkCount); // import still owns its lease
        }
        finally
        {
            host.Continue.TrySetResult();
            Assert.Equal(0, await guest.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        Assert.Equal(1, host.Resources.ClunkCount);
        Assert.Equal(1ul, (await host.Resources.StatAsync(host.Opened.Resource, default)).Length);
    }

    [Theory]
    [InlineData(-1, 1, 16, 128, 1)]
    [InlineData(65532, 1, 16, 128, 1)]
    [InlineData(0, -1, 16, 128, 1)]
    [InlineData(0, 1, 65534, 128, 1)]
    [InlineData(0, 1, 16, -1, 1)]
    [InlineData(0, 1, 16, 65535, 2)]
    [InlineData(0, 1, 16, 128, -1)]
    public async Task InvalidGuestRangesCauseNoProviderWrite(int vectors, int count, int result, int buffer, int length)
    {
        await using var host = await FileHost.CreateAsync();
        using var instance = CreateModule().Compile<Guest>()(host.Imports);
        host.Memory = instance.Exports.memory;
        host.SetVector(buffer, length);
        Assert.Equal(21, await OnWorker(() => instance.Exports.Write(host.Fd, vectors, count, result))); // FAULT
        Assert.Equal(0, host.WriteCount);
        Assert.Empty((await host.Data.ReadAsync(host.Opened, 0, 100, default)).ToArray());
    }

    private static Task<int> OnWorker(Func<int> run)
        => Task.Factory.StartNew(run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static Module CreateModule()
    {
        var module = new Module();
        WebAssemblyValueType i32 = WebAssemblyValueType.Int32;
        module.Types.Add(new WebAssemblyType { Parameters = [i32, i32, i32, i32], Returns = [i32] });
        module.Types.Add(new WebAssemblyType { Parameters = [i32, i32, i32, WebAssemblyValueType.Int64, i32], Returns = [i32] });
        module.Types.Add(new WebAssemblyType { Parameters = [i32], Returns = [i32] });
        string[] imports = ["fd_write", "fd_pread", "fd_close"];
        string[] exports = ["Write", "Read", "Close"];
        for (uint i = 0; i < imports.Length; i++)
        {
            module.Imports.Add(new Import.Function("wasi_snapshot_preview1", imports[i], i));
            module.Functions.Add(new Function { Type = i });
            var code = new List<Instruction>();
            for (uint p = 0; p < module.Types[(int)i].Parameters.Count; p++) code.Add(new LocalGet(p));
            code.Add(new Call(i));
            code.Add(new End());
            module.Codes.Add(new FunctionBody { Code = code });
            module.Exports.Add(new Export { Name = exports[i], Kind = ExternalKind.Function, Index = i + 3 });
        }

        module.Memories.Add(new Memory(1, 2));
        module.Exports.Add(new Export { Name = "memory", Kind = ExternalKind.Memory, Index = 0 });
        return module;
    }

    public abstract class Guest
    {
        public abstract UnmanagedMemory memory { get; }
        public abstract int Write(int fd, int vectors, int count, int result);
        public abstract int Read(int fd, int vectors, int count, long offset, int result);
        public abstract int Close(int fd);
    }

    private sealed class FileHost : IAsyncDisposable
    {
        internal MemoryDataResources Resources { get; } = new();
        internal DescriptorGroup Descriptors { get; } = new();
        internal LocalNamespaceDataPlane Data { get; private set; } = null!;
        internal ResourceOpenHandle Opened { get; private set; } = null!;
        internal UnmanagedMemory Memory { get; set; } = null!;
        internal int Fd { get; private set; }
        internal bool PauseWrite { get; set; }
        internal int WriteCount { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ulong sequence;

        internal ImportDictionary Imports => new()
        {
            { "wasi_snapshot_preview1", "fd_write", new FunctionImport(new Func<int, int, int, int, int>(Write)) },
            { "wasi_snapshot_preview1", "fd_pread", new FunctionImport(new Func<int, int, int, long, int, int>(Read)) },
            { "wasi_snapshot_preview1", "fd_close", new FunctionImport(new Func<int, int>(Close)) },
        };

        internal static async Task<FileHost> CreateAsync()
        {
            var host = new FileHost();
            ResourceHandle root = host.Resources.Directory("wasm-fixture", "data");
            host.Data = new LocalNamespaceDataPlane(new MountTable(), host.Resources);
            NamespaceChannel channel = await host.Data.AttachAsync(root, default);
            NamespaceWalkResult walked = await host.Data.WalkAsync(channel, ["data"], default);
            host.Opened = await host.Data.OpenAsync(walked.Channel, 2, host.Context(), default);
            host.Fd = host.Descriptors.Install(host.Opened, () => host.Data.ClunkAsync(host.Opened, host.Context(), default));
            return host;
        }

        internal void SetVector(int buffer, int length)
        {
            Marshal.WriteInt32(Memory.Start, buffer);
            Marshal.WriteInt32(Memory.Start + 4, length);
        }

        private ResourceOperationContext Context() => new(new ResourceOperationId("wasm-fixture", ++sequence), 1, "test");

        private bool Range(int address, ulong length) => (ulong)unchecked((uint)address) + length <= Memory.Size;

        private bool Vector(int vectors, int count, int result, out int buffer, out int length)
        {
            buffer = length = 0;
            if (count != 1 || !Range(vectors, 8) || !Range(result, 4)) return false;
            buffer = Marshal.ReadInt32(Memory.Start + vectors);
            length = Marshal.ReadInt32(Memory.Start + vectors + 4);
            return Range(buffer, unchecked((uint)length));
        }

        private int Write(int fd, int vectors, int count, int result)
        {
            if (!Vector(vectors, count, result, out int buffer, out int length)) return 21;
            byte[] copy = new byte[length];
            Marshal.Copy(Memory.Start + buffer, copy, 0, length);
            // Only this dedicated test worker blocks. Never call this on an Orleans turn.
            uint written = WriteAsync(fd, copy).GetAwaiter().GetResult();
            Marshal.WriteInt32(Memory.Start + result, checked((int)written));
            return 0;
        }

        private async Task<uint> WriteAsync(int fd, byte[] copy)
        {
            await using DescriptorLease lease = Descriptors.Acquire(fd);
            if (PauseWrite)
            {
                Entered.TrySetResult();
                await Continue.Task.ConfigureAwait(false);
            }

            WriteCount++;
            return await Data.WriteAsync(lease.Handle, 0, copy, Context(), default).ConfigureAwait(false);
        }

        private int Read(int fd, int vectors, int count, long offset, int result)
        {
            if (!Vector(vectors, count, result, out int buffer, out int length)) return 21;
            byte[] bytes = ReadAsync(fd, unchecked((ulong)offset), checked((uint)length)).GetAwaiter().GetResult();
            Marshal.Copy(bytes, 0, Memory.Start + buffer, bytes.Length);
            Marshal.WriteInt32(Memory.Start + result, bytes.Length);
            return 0;
        }

        private async Task<byte[]> ReadAsync(int fd, ulong offset, uint count)
        {
            await using DescriptorLease lease = Descriptors.Acquire(fd);
            return (await Data.ReadAsync(lease.Handle, offset, count, default).ConfigureAwait(false)).ToArray();
        }

        private int Close(int fd)
        {
            try { Descriptors.CloseAsync(fd).AsTask().GetAwaiter().GetResult(); return 0; }
            catch (ArgumentException) { return 8; }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (DescriptorSlot slot in Descriptors.Snapshot()) await Descriptors.CloseAsync(slot.Number);
        }
    }
}
