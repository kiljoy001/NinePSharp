using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NinePSharp.Backends.PowerShell;
using NinePSharp.Messages;
using NinePSharp.Server.Utils;
using Xunit;

namespace NinePSharp.Tests
{
    public class GridSideEffectTests
    {
        [Fact]
        public async Task PowerShell_Job_Lifecycle_Ensures_SideEffect_Cleanup()
        {
            var fs = new PowerShellFileSystem();
            string jobId = $"cleanup-test-{Guid.NewGuid():N}";
            
            // 1. Create job
            await fs.WalkAsync(new Twalk(1, 1, 2, new[] { "jobs" }));
            await fs.MkdirAsync(new Tmkdir(0, 1, 2, jobId, 0755, 0));
            await fs.WalkAsync(new Twalk(1, 2, 3, new[] { jobId, "script.ps1" }));

            // 2. Start a script that outputs its own PID and then sleeps
            string script = "echo $pid; Start-Sleep -Seconds 10";
            await fs.WriteAsync(new Twrite(1, 3, 0, Encoding.UTF8.GetBytes(script)));

            // 3. Capture the PID from the output file
            await Task.Delay(2000); // Wait for startup
            var outFs = fs.Clone();
            await outFs.WalkAsync(new Twalk(1, 3, 4, new[] { "output.json" }));
            var readRes = await outFs.ReadAsync(new Tread(1, 4, 0, 1024));
            string output = Encoding.UTF8.GetString(readRes.Data.ToArray());
            
            // Extract PID (it will be in a JSON array)
            int pid = int.Parse(new string(output.Where(char.IsDigit).ToArray()));
            
            // ASSERT SIDE EFFECT: Process must exist in OS
            Process.GetProcessById(pid).Should().NotBeNull();

            // 4. KILL the job via Tremove
            await fs.WalkAsync(new Twalk(1, 1, 5, new[] { "jobs", jobId }));
            await fs.RemoveAsync(new Tremove(1, 5));

            // 5. ASSERT SIDE EFFECT: Process must be GONE from OS immediately
            await Task.Delay(500); // Give OS a tiny window to reap
            Action act = () => Process.GetProcessById(pid);
            act.Should().Throw<ArgumentException>("Process must be terminated by the server when the job is removed.");

            // 6. ASSERT SIDE EFFECT: Temp files must be cleaned up
            var tempFiles = Directory.GetFiles(Path.GetTempPath(), $"*.ps1");
            tempFiles.Any(f => File.ReadAllText(f).Contains(script)).Should().BeFalse("Temporary script files must be deleted after job completion or removal.");
        }

        [Fact]
        public async Task LuxVault_Store_SideEffect_Is_Opaque()
        {
            const string secretName = "side-effect-secret";
            const string password = "opaque-pass";
            byte[] plaintext = Encoding.UTF8.GetBytes("sensitive-data-12345");

            using var ss = new System.Security.SecureString();
            foreach (var c in password) ss.AppendChar(c);
            ss.MakeReadOnly();

            // 1. Store the secret
            LuxVault.StoreSecret(secretName, plaintext, ss);

            // 2. Predict the path
            byte[] nameBytes = Encoding.UTF8.GetBytes(secretName);
            byte[] seed = new byte[32];
            LuxVault.DeriveSeed(password, nameBytes, seed);
            string hiddenId = LuxVault.GenerateHiddenId(seed);
            string path = LuxVault.GetVaultPath($"secret_{hiddenId}.vlt");

            // 3. ASSERT SIDE EFFECT: File exists and is NOT readable as cleartext
            File.Exists(path).Should().BeTrue();
            byte[] diskData = File.ReadAllBytes(path);
            Encoding.UTF8.GetString(diskData).Should().NotContain("sensitive-data-12345", "Vault file must be encrypted on disk.");
            
            // 4. Cleanup side effect
            File.Delete(path);
        }
    }
}
