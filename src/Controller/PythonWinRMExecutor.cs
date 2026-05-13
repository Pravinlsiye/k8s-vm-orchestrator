using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VMJobOrchestrator
{
    /// <summary>
    /// WinRM executor that uses Python's pywinrm for Linux-to-Windows connectivity
    /// </summary>
    public class PythonWinRMExecutor : VMExecutor, IVMExecutor
    {
        private readonly ILogger<PythonWinRMExecutor> _logger;
        private readonly string _plainPassword;

        public PythonWinRMExecutor(ILogger<PythonWinRMExecutor> logger, string username, string password) 
            : base(logger, username, password)
        {
            _logger = logger;
            _plainPassword = password; // Store plain password for Python
        }

        public override async Task<ExecutionResult> ExecuteRemoteAsync(
            string host,
            int port,
            string command,
            List<string> args,
            string workingDir,
            List<EnvVar> envVars,
            TimeSpan timeout)
        {
            _logger.LogInformation($"Executing on {host}:{port} using Python pywinrm");
            
            args ??= new List<string>();
            
            try
            {
                // Build the remote command
                var remoteScript = BuildRemoteScript(command, args, workingDir, envVars);
                
                // Create Python script that uses pywinrm
                var pythonScript = $@"
import winrm
import sys

try:
    # Create WinRM session
    session = winrm.Session('{host}:{port}', auth=('{_username}', '{_plainPassword}'), transport='basic')
    
    # Execute PowerShell command
    ps_script = '''{remoteScript}'''
    
    result = session.run_ps(ps_script)
    
    if result.status_code == 0:
        print('EXECUTION_SUCCESS')
        print(result.std_out.decode('utf-8'))
    else:
        print('EXECUTION_FAILED')
        print(result.std_err.decode('utf-8'))
        sys.exit(result.status_code)
        
except Exception as e:
    print(f'EXECUTION_FAILED: {{e}}')
    sys.exit(1)
";

                // Log the Python script for debugging
                _logger.LogDebug($"Python script:\n{pythonScript}");
                
                // Save script to file for easier debugging
                var scriptPath = "/tmp/winrm_script.py";
                await System.IO.File.WriteAllTextAsync(scriptPath, pythonScript);
                
                // Execute via Python process
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = scriptPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (sender, e) => 
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.AppendLine(e.Data);
                };

                process.ErrorDataReceived += (sender, e) => 
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        error.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds));

                if (!completed)
                {
                    process.Kill();
                    return new ExecutionResult
                    {
                        Success = false,
                        ExitCode = -1,
                        StandardOutput = output.ToString(),
                        StandardError = "Execution timed out",
                        Error = "Timeout"
                    };
                }

                var outputStr = output.ToString();
                var errorStr = error.ToString();
                
                // Check if the Python script itself succeeded
                var pythonSuccess = process.ExitCode == 0;
                
                // Extract the actual job result from the output
                bool jobSuccess = false;
                string jobOutput = "";
                string jobError = "";
                int jobExitCode = -1;
                
                if (pythonSuccess && outputStr.Contains("EXECUTION_SUCCESS"))
                {
                    jobSuccess = true;
                    jobExitCode = 0;
                    // Extract output after EXECUTION_SUCCESS
                    var lines = outputStr.Split('\n');
                    bool foundMarker = false;
                    foreach (var line in lines)
                    {
                        if (foundMarker && !string.IsNullOrWhiteSpace(line))
                        {
                            jobOutput += line + "\n";
                        }
                        else if (line.Contains("EXECUTION_SUCCESS"))
                        {
                            foundMarker = true;
                        }
                    }
                    jobOutput = jobOutput.Trim();
                }
                else if (pythonSuccess && outputStr.Contains("EXECUTION_FAILED"))
                {
                    jobSuccess = false;
                    // Extract error after EXECUTION_FAILED
                    var lines = outputStr.Split('\n');
                    bool foundMarker = false;
                    foreach (var line in lines)
                    {
                        if (foundMarker && !string.IsNullOrWhiteSpace(line))
                        {
                            jobError += line + "\n";
                        }
                        else if (line.Contains("EXECUTION_FAILED"))
                        {
                            foundMarker = true;
                        }
                    }
                    jobError = jobError.Trim();
                }
                else
                {
                    // Python script itself failed
                    jobSuccess = false;
                    jobError = errorStr;
                }

                return new ExecutionResult
                {
                    Success = jobSuccess,
                    ExitCode = jobExitCode,
                    StandardOutput = jobOutput,
                    StandardError = jobError,
                    ResultPath = workingDir != null ? $"{workingDir}\\job-output.txt" : null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to execute on {host}");
                return new ExecutionResult
                {
                    Success = false,
                    ExitCode = -1,
                    StandardOutput = "",
                    StandardError = ex.Message,
                    Error = ex.ToString()
                };
            }
        }

        private string BuildRemoteScript(string command, List<string> args, string workingDir, List<EnvVar> envVars)
        {
            var script = new StringBuilder();

            // Set working directory
            if (!string.IsNullOrEmpty(workingDir))
            {
                script.AppendLine($"Set-Location '{workingDir}'");
            }

            // Set environment variables
            if (envVars != null)
            {
                foreach (var env in envVars)
                {
                    script.AppendLine($"$env:{env.Name} = '{env.Value}'");
                }
            }

            // Build command with arguments
            // Special handling for PowerShell commands
            if (command.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                // If command is PowerShell and args contains -Command, extract the actual script
                if (args != null && args.Count >= 2 && args[0] == "-Command")
                {
                    // Skip "-Command" and join the rest as the script
                    var scriptContent = string.Join(" ", args.Skip(1));
                    // Escape backslashes for Python string
                    scriptContent = scriptContent.Replace("\\", "\\\\");
                    script.AppendLine(scriptContent);
                }
                else
                {
                    // Fallback: just execute PowerShell with args
                    script.AppendLine($"& '{command}' {string.Join(" ", args ?? new List<string>())}");
                }
            }
            else
            {
                // For non-PowerShell commands, execute normally
                if (args != null && args.Count > 0)
                {
                    var argsStr = string.Join(" ", args);
                    script.AppendLine($"& '{command}' {argsStr}");
                }
                else
                {
                    script.AppendLine($"& '{command}'");
                }
            }

            return script.ToString();
        }
    }
}
