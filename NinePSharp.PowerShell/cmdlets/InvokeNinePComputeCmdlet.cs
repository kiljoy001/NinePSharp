using System;
using System.Management.Automation;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NinePSharp.PowerShell.Internal;

namespace NinePSharp.PowerShell.Cmdlets;

[Cmdlet(VerbsLifecycle.Invoke, "NinePCompute")]
[OutputType(typeof(object))]
public class InvokeNinePComputeCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)]
    public ScriptBlock Script { get; set; } = null!;

    [Parameter(Position = 1)]
    public string NinePHost { get; set; } = "localhost";

    [Parameter(Position = 2)]
    public int Port { get; set; } = 564;

    [Parameter]
    public string Export { get; set; } = "ps";

    protected override void ProcessRecord()
    {
        using var client = new NinePClient(NinePHost, Port);
        
        var task = Task.Run(async () =>
        {
            await client.VersionAsync();
            await client.AttachAsync(1, Environment.UserName, Export);

            // 1. Create a job ID
            string jobId = "job-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            
            // 2. Walk to /jobs
            await client.WalkAsync(1, 2, new[] { "jobs" });
            
            // 3. Create job directory
            await client.MkdirAsync(2, jobId, 0755);
            
            // 4. Walk into job directory
            await client.WalkAsync(2, 3, new[] { jobId });
            
            // 5. Upload script
            var scriptFs = 4u;
            await client.WalkAsync(3, scriptFs, new[] { "script.ps1" });
            await client.WriteAsync(scriptFs, 0, Encoding.UTF8.GetBytes(Script.ToString()));
            
            // 6. Poll for completion
            var statusFs = 5u;
            await client.WalkAsync(3, statusFs, new[] { "status" });
            
            string status = "";
            int retries = 0;
            while (retries < 100)
            {
                var bytes = await client.ReadAsync(statusFs, 0, 100);
                status = Encoding.UTF8.GetString(bytes).Trim();
                
                if (status == "Completed" || status == "Failed") break;
                
                await Task.Delay(500);
                retries++;
            }

            if (status == "Failed")
            {
                var errorFs = 6u;
                await client.WalkAsync(3, errorFs, new[] { "errors" });
                var errBytes = await client.ReadAsync(errorFs, 0, 8192);
                throw new Exception("Remote execution failed: " + Encoding.UTF8.GetString(errBytes));
            }

            // 7. Read output
            var outputFs = 7u;
            await client.WalkAsync(3, outputFs, new[] { "output.json" });
            var outputBytes = await client.ReadAsync(outputFs, 0, 65536);
            
            return Encoding.UTF8.GetString(outputBytes);
        });

        string json = task.GetAwaiter().GetResult();
        
        if (!string.IsNullOrWhiteSpace(json))
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var objects = JsonSerializer.Deserialize<object[]>(json, options);
            if (objects != null)
            {
                foreach (var obj in objects)
                {
                    WriteObject(obj);
                }
            }
        }
    }
}
