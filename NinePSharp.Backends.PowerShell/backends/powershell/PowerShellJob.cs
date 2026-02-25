using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NinePSharp.Backends.PowerShell;

public class PowerShellJob
{
    public string Id { get; }
    public string? Script { get; set; }
    public StringBuilder Output { get; } = new();
    public StringBuilder Errors { get; } = new();
    public string Status { get; private set; } = "Created";
    
    private Process? _activeProcess;
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

        // Plan 9 Way: Delegate to an isolated subprocess
        try
        {
            var tempScript = Path.GetTempFileName() + ".ps1";
            await File.WriteAllTextAsync(tempScript, Script);

            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/pwsh",
                Arguments = $"-NoProfile -NonInteractive -File \"{tempScript}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Hard Throttling: Set process priority low so it doesn't "crush" the OS
            _activeProcess = new Process { StartInfo = startInfo };
            _activeProcess.OutputDataReceived += (s, e) => { if (e.Data != null) lock(_lock) Output.AppendLine(e.Data); };
            _activeProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) lock(_lock) Errors.AppendLine(e.Data); };

            _activeProcess.Start();
            
            // Set priority after start
            try { _activeProcess.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { /* Might fail on some OS */ }

            _activeProcess.BeginOutputReadLine();
            _activeProcess.BeginErrorReadLine();

            await _activeProcess.WaitForExitAsync();

            lock (_lock)
            {
                Status = _activeProcess.ExitCode == 0 ? "Completed" : "Failed";
                if (_activeProcess.ExitCode != 0) 
                    Errors.AppendLine($"Process exited with code: {_activeProcess.ExitCode}");
            }

            File.Delete(tempScript);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                Status = "Failed";
                Errors.AppendLine($"Critical Launcher Error: {ex.Message}");
            }
        }
    }

    public void Kill()
    {
        lock (_lock)
        {
            try
            {
                if (_activeProcess != null && !_activeProcess.HasExited)
                {
                    _activeProcess.Kill(entireProcessTree: true);
                    Status = "Cancelled";
                }
            }
            catch { /* Ignore */ }
        }
    }
}
