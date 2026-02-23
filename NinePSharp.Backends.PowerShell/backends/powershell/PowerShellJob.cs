using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NinePSharp.Backends.PowerShell;

public class PowerShellJob
{
    public string Id { get; }
    public string? Script { get; set; }
    public StringBuilder Output { get; } = new();
    public StringBuilder Errors { get; } = new();
    public string Status { get; private set; } = "Created";
    
    private readonly object _lock = new();

    public PowerShellJob(string id)
    {
        Id = id;
    }

    public async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Script)) return;

        lock (_lock)
        {
            if (Status == "Running") return;
            Status = "Running";
            Output.Clear();
            Errors.Clear();
        }

        try
        {
            await Task.Run(() =>
            {
                // Create a default runspace
                using var runspace = RunspaceFactory.CreateRunspace();
                runspace.Open();

                using var ps = System.Management.Automation.PowerShell.Create();
                ps.Runspace = runspace;
                ps.AddScript(Script);

                // Execute the script
                var results = ps.Invoke();

                lock (_lock)
                {
                    if (ps.HadErrors)
                    {
                        foreach (var error in ps.Streams.Error)
                        {
                            Errors.AppendLine(error.ToString());
                        }
                    }

                    // Convert results to JSON for "Object-Oriented 9P"
                    if (results.Count > 0)
                    {
                        var serializableResults = results.Select(r => {
                            if (r.BaseObject is string || r.BaseObject.GetType().IsPrimitive) 
                                return r.BaseObject;
                            
                            var dict = new Dictionary<string, object?>();
                            foreach (var prop in r.Properties)
                            {
                                try { dict[prop.Name] = prop.Value; } catch { /* Ignore non-readable */ }
                            }
                            return dict.Count > 0 ? (object)dict : r.BaseObject;
                        });

                        var json = JsonSerializer.Serialize(serializableResults, new JsonSerializerOptions 
                        { 
                            WriteIndented = true 
                        });
                        Output.Append(json);
                    }
                }
            });

            lock (_lock) Status = "Completed";
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                Status = "Failed";
                Errors.AppendLine($"Critical Error: {ex.Message}");
            }
        }
    }
}
