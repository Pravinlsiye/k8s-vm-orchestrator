using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VMJobOrchestrator
{
    public class VMExecutor
    {
        private readonly ILogger<VMExecutor> _logger;
        protected readonly string _username;
        protected readonly SecureString _password;

        public VMExecutor(ILogger<VMExecutor> logger, string username, string password)
        {
            _logger = logger;
            _username = username;
            _password = ConvertToSecureString(password);
        }

        public virtual async Task<ExecutionResult> ExecuteRemoteAsync(
            string host,
            int port,
            string command,
            List<string> args,
            string workingDir,
            List<EnvVar> envVars,
            TimeSpan timeout)
        {
            args ??= new List<string>();
            envVars ??= new List<EnvVar>();

            _logger.LogInformation($"Executing on {host}:{port} - {command} {string.Join(" ", args)}");

            try
            {
                // Create WinRM connection
                var connectionInfo = new WSManConnectionInfo
                {
                    ComputerName = host,
                    Port = port,
                    Scheme = "http", // Use HTTPS in production
                    AuthenticationMechanism = AuthenticationMechanism.Negotiate,
                    Credential = new PSCredential(_username, _password),
                    OperationTimeout = (int)timeout.TotalMilliseconds,
                    OpenTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds
                };

                using var runspace = RunspaceFactory.CreateRunspace(connectionInfo);
                await Task.Run(() => runspace.Open());

                using var powershell = PowerShell.Create();
                powershell.Runspace = runspace;

                // Build PowerShell script
                var script = BuildPowerShellScript(command, args, workingDir, envVars);
                _logger.LogDebug($"PowerShell script:\n{script}");

                powershell.AddScript(script);

                // Execute
                var startTime = DateTime.UtcNow;
                var results = await Task.Run(() => powershell.Invoke());
                var executionTime = DateTime.UtcNow - startTime;

                // Collect output
                var stdout = new StringBuilder();
                foreach (var result in results)
                {
                    stdout.AppendLine(result?.ToString());
                }

                var stderr = new StringBuilder();
                if (powershell.HadErrors)
                {
                    foreach (var error in powershell.Streams.Error)
                    {
                        stderr.AppendLine(error.ToString());
                    }
                }

                // Extract exit code
                var exitCode = ExtractExitCode(stdout.ToString());
                var success = exitCode == 0 && !powershell.HadErrors;

                _logger.LogInformation($"Execution completed in {executionTime.TotalSeconds:F2}s with exit code {exitCode}");

                // Extract result path if present
                var resultPath = ExtractResultPath(stdout.ToString());

                return new ExecutionResult
                {
                    Success = success,
                    ExitCode = exitCode,
                    StandardOutput = stdout.ToString(),
                    StandardError = stderr.ToString(),
                    ExecutionTime = executionTime,
                    ResultPath = resultPath
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command");
                return new ExecutionResult
                {
                    Success = false,
                    ExitCode = -1,
                    StandardOutput = string.Empty,
                    StandardError = ex.Message,
                    Error = ex.Message
                };
            }
        }

        private string BuildPowerShellScript(
            string command,
            List<string> args,
            string workingDir,
            List<EnvVar> envVars)
        {
            var scriptBuilder = new StringBuilder();

            // Set error action preference
            scriptBuilder.AppendLine("$ErrorActionPreference = 'Continue'");

            // Set environment variables
            foreach (var envVar in envVars)
            {
                if (!string.IsNullOrEmpty(envVar.Name) && !string.IsNullOrEmpty(envVar.Value))
                {
                    var escapedValue = envVar.Value.Replace("'", "''");
                    scriptBuilder.AppendLine($"$env:{envVar.Name} = '{escapedValue}'");
                }
            }

            // Change working directory if specified
            if (!string.IsNullOrEmpty(workingDir))
            {
                scriptBuilder.AppendLine($"if (-not (Test-Path '{workingDir}')) {{ New-Item -ItemType Directory -Force -Path '{workingDir}' | Out-Null }}");
                scriptBuilder.AppendLine($"Set-Location '{workingDir}'");
            }

            // Build command
            string cmdLine;
            if (command.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                // PowerShell script
                cmdLine = $"& '{command}'";
                if (args.Count > 0)
                {
                    var argsStr = string.Join(" ", args.ConvertAll(arg => $"'{arg}'"));
                    cmdLine += $" {argsStr}";
                }
            }
            else if (command.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) || 
                     command.Equals("powershell", StringComparison.OrdinalIgnoreCase))
            {
                // PowerShell command with args
                cmdLine = args.Count > 0 ? string.Join(" ", args) : "";
            }
            else
            {
                // Regular executable
                cmdLine = $"& '{command}'";
                if (args.Count > 0)
                {
                    var argsStr = string.Join(" ", args.ConvertAll(arg => $"'{arg}'"));
                    cmdLine += $" {argsStr}";
                }
            }

            if (!string.IsNullOrEmpty(cmdLine))
            {
                scriptBuilder.AppendLine(cmdLine);
            }

            // Capture exit code
            scriptBuilder.AppendLine("$exitCode = $LASTEXITCODE");
            scriptBuilder.AppendLine("if ($null -eq $exitCode) { $exitCode = 0 }");
            scriptBuilder.AppendLine("Write-Output \"EXIT_CODE:$exitCode\"");
            scriptBuilder.AppendLine("exit $exitCode");

            return scriptBuilder.ToString();
        }

        private int ExtractExitCode(string stdout)
        {
            // Look for EXIT_CODE:n pattern
            var match = System.Text.RegularExpressions.Regex.Match(stdout, @"EXIT_CODE:(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var exitCode))
            {
                return exitCode;
            }
            return 0;
        }

        private string ExtractResultPath(string stdout)
        {
            // Look for RESULT_PATH:path pattern
            var match = System.Text.RegularExpressions.Regex.Match(stdout, @"RESULT_PATH:(.+?)(?:\r?\n|$)");
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            return null;
        }

        private SecureString ConvertToSecureString(string password)
        {
            var secureString = new SecureString();
            foreach (char c in password)
            {
                secureString.AppendChar(c);
            }
            secureString.MakeReadOnly();
            return secureString;
        }
    }

    public class ExecutionResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public string ResultPath { get; set; }
        public string Error { get; set; }
    }
}
