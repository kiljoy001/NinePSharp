using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Wasmtime;

namespace NinePSharp.Backends.Compute;

public class WasmJob
{
    public string Id { get; }
    public byte[]? WasmBinary { get; set; }
    public StringBuilder Output { get; } = new();
    public string Status { get; private set; } = "Created";
    
    private readonly object _lock = new();

    public WasmJob(string id)
    {
        Id = id;
    }

    public async Task RunAsync()
    {
        if (WasmBinary == null) throw new InvalidOperationException("No WASM binary provided.");

        lock (_lock)
        {
            if (Status == "Running") return;
            Status = "Running";
            Output.Clear();
        }

        try
        {
            await Task.Run(() =>
            {
                using var engine = new Engine();
                using var module = Module.FromBytes(engine, Id, WasmBinary);
                using var linker = new Linker(engine);
                using var store = new Store(engine);

                linker.DefineWasi();
                
                string tempFile = Path.GetTempFileName();
                try
                {
                    store.SetWasiConfiguration(new WasiConfiguration()
                        .WithStandardOutput(tempFile)
                        .WithStandardError(tempFile));

                    var instance = linker.Instantiate(store, module);
                    var start = instance.GetFunction("_start");
                    
                    if (start == null)
                    {
                        lock (_lock) Output.Append("Error: Entry point '_start' not found.");
                        return;
                    }

                    try 
                    {
                        start.Invoke();
                    }
                    catch (Exception ex)
                    {
                        lock (_lock) Output.Append($"Runtime Error: {ex.Message}");
                    }

                    var result = File.ReadAllText(tempFile);
                    lock (_lock)
                    {
                        Output.Append(result);
                    }
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });

            lock (_lock) Status = "Completed";
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                Status = "Failed";
                Output.Append($"Critical Error: {ex.Message}");
            }
        }
    }
}
