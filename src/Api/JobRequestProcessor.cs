using System.Text.RegularExpressions;
using VMJobAPI.Models;

namespace VMJobAPI.Services;

public class JobRequestProcessor
{
    private readonly ILogger<JobRequestProcessor> _logger;
    private readonly Dictionary<string, JobTemplate> _templates;

    public JobRequestProcessor(ILogger<JobRequestProcessor> logger)
    {
        _logger = logger;
        _templates = LoadTemplates();
    }

    public VMJobSpec ProcessRequest(CreateVMJobRequest request)
    {
        // If legacy Spec is provided, use it directly
        if (request.Spec != null)
        {
            return request.Spec;
        }

        // Process stage-based format
        if (request.Stages != null && request.Stages.Any())
        {
            return ProcessStages(request);
        }

        // Process template if specified
        if (!string.IsNullOrEmpty(request.Template))
        {
            return ProcessTemplate(request);
        }

        // Process single command
        if (!string.IsNullOrEmpty(request.Command))
        {
            return ProcessSingleCommand(request);
        }

        // Process multiple commands
        if (request.Commands != null && request.Commands.Any())
        {
            return ProcessMultipleCommands(request);
        }

        // Process script
        if (request.Script != null)
        {
            return ProcessScript(request);
        }

        // Process pipeline
        if (request.Pipeline != null && request.Pipeline.Any())
        {
            return ProcessPipeline(request);
        }

        throw new ArgumentException("Invalid job format. Specify 'command', 'commands', 'script', 'pipeline', 'stages', or 'template'.");
    }

    private VMJobSpec ProcessSingleCommand(CreateVMJobRequest request)
    {
        var (command, args) = ParseCommand(request.Command!);
        
        return new VMJobSpec
        {
            VMSelector = new VMSelector
            {
                OS = "windows",
                NodeName = request.Advanced?.TargetVM,
                Tags = request.Advanced?.Tags
            },
            Command = command,
            Args = args,
            WorkingDir = request.Advanced?.WorkingDir ?? "C:\\",
            Timeout = request.Advanced?.Timeout ?? "2h",
            Priority = request.Advanced?.Priority ?? 50
        };
    }

    private VMJobSpec ProcessMultipleCommands(CreateVMJobRequest request)
    {
        // Create a PowerShell script that executes commands with step logging
        var scriptLines = new List<string>
        {
            "$logFile = \"C:\\vmjob-results\\job-" + request.Name + "-$(Get-Date -Format 'yyyyMMddHHmmss').log\"",
            "New-Item -ItemType Directory -Force -Path \"C:\\vmjob-results\" | Out-Null",
            "",
            "function Log-Step {",
            "    param($StepName, $Command, $ExitCode, $Output)",
            "    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'",
            "    $logEntry = @{",
            "        Step = $StepName",
            "        Command = $Command",
            "        ExitCode = $ExitCode",
            "        Output = $Output",
            "        Timestamp = $timestamp",
            "    }",
            "    $logEntry | ConvertTo-Json -Compress | Add-Content $logFile",
            "}",
            "",
            "$allOutputs = @()",
            "$failedSteps = 0"
        };

        // Add each command as a step
        for (int i = 0; i < request.Commands!.Count; i++)
        {
            var stepName = $"Step{i + 1}";
            var command = request.Commands[i];
            
            scriptLines.AddRange(new[]
            {
                "",
                $"# {stepName}: {command}",
                $"Write-Host \"[{stepName}] Executing: {command}\" -ForegroundColor Cyan",
                $"$output = & cmd /c \"{command.Replace("\"", "\"\"")}\" 2>&1 | Out-String",
                "$exitCode = $LASTEXITCODE",
                "$allOutputs += \"[{stepName}] {command}`n$output`n\"",
                $"Log-Step -StepName '{stepName}' -Command '{command}' -ExitCode $exitCode -Output $output",
                "",
                "if ($exitCode -ne 0) {",
                $"    Write-Host \"[{stepName}] Failed with exit code: $exitCode\" -ForegroundColor Red",
                "    $failedSteps++",
                "} else {",
                $"    Write-Host \"[{stepName}] Completed successfully\" -ForegroundColor Green",
                "}"
            });
        }

        // Add summary
        scriptLines.AddRange(new[]
        {
            "",
            "# Summary",
            "Write-Host \"`n=== Job Summary ===\" -ForegroundColor Yellow",
            "Write-Host \"Total steps: " + request.Commands.Count + "\"",
            "Write-Host \"Failed steps: $failedSteps\"",
            "Write-Host \"Log file: $logFile\"",
            "",
            "# Output all results",
            "$allOutputs -join \"`n\"",
            "",
            "# Exit with error if any step failed",
            "if ($failedSteps -gt 0) { exit 1 }"
        });

        var script = string.Join("\n", scriptLines);

        return new VMJobSpec
        {
            VMSelector = new VMSelector
            {
                OS = "windows",
                NodeName = request.Advanced?.TargetVM,
                Tags = request.Advanced?.Tags
            },
            Command = "powershell.exe",
            Args = new List<string> { "-ExecutionPolicy", "Bypass", "-Command", script },
            WorkingDir = request.Advanced?.WorkingDir ?? "C:\\",
            Timeout = request.Advanced?.Timeout ?? "2h",
            Priority = request.Advanced?.Priority ?? 50
        };
    }

    private VMJobSpec ProcessScript(CreateVMJobRequest request)
    {
        var script = request.Script!;
        
        return script.Type.ToLower() switch
        {
            "powershell" => new VMJobSpec
            {
                VMSelector = new VMSelector { OS = "windows" },
                Command = "powershell.exe",
                Args = new List<string> { "-ExecutionPolicy", "Bypass", "-Command", script.Content },
                WorkingDir = request.Advanced?.WorkingDir ?? "C:\\",
                Timeout = request.Advanced?.Timeout ?? "2h",
                Priority = request.Advanced?.Priority ?? 50
            },
            "batch" or "cmd" => new VMJobSpec
            {
                VMSelector = new VMSelector { OS = "windows" },
                Command = "cmd.exe",
                Args = new List<string> { "/c", script.Content },
                WorkingDir = request.Advanced?.WorkingDir ?? "C:\\",
                Timeout = request.Advanced?.Timeout ?? "2h",
                Priority = request.Advanced?.Priority ?? 50
            },
            "python" => new VMJobSpec
            {
                VMSelector = new VMSelector { OS = "windows" },
                Command = "python.exe",
                Args = new List<string> { "-c", script.Content },
                WorkingDir = request.Advanced?.WorkingDir ?? "C:\\",
                Timeout = request.Advanced?.Timeout ?? "2h",
                Priority = request.Advanced?.Priority ?? 50
            },
            _ => throw new ArgumentException($"Unsupported script type: {script.Type}")
        };
    }

    private VMJobSpec ProcessPipeline(CreateVMJobRequest request)
    {
        // Convert pipeline to a PowerShell script with advanced error handling
        var scriptLines = new List<string>
        {
            "$ErrorActionPreference = 'Continue'",
            "$pipelineLog = \"C:\\vmjob-results\\pipeline-" + request.Name + "-$(Get-Date -Format 'yyyyMMddHHmmss').log\"",
            "New-Item -ItemType Directory -Force -Path \"C:\\vmjob-results\" | Out-Null",
            "",
            "$pipelineResults = @()"
        };

        foreach (var step in request.Pipeline!)
        {
            var stepScript = "";
            
            if (!string.IsNullOrEmpty(step.Command))
            {
                stepScript = step.Command;
            }
            else if (step.Script != null)
            {
                stepScript = step.Script.Content;
            }

            scriptLines.AddRange(new[]
            {
                "",
                $"# Pipeline Step: {step.Name}",
                $"Write-Host \"`n[PIPELINE: {step.Name}] Starting...\" -ForegroundColor Magenta",
                "$stepStart = Get-Date",
                "try {",
                $"    $stepOutput = {(step.Script?.Type == "powershell" ? stepScript : $"& cmd /c \"{stepScript}\"")} 2>&1 | Out-String",
                "    $stepExitCode = $LASTEXITCODE",
                "} catch {",
                "    $stepOutput = $_.Exception.Message",
                "    $stepExitCode = 1",
                "}",
                "$stepEnd = Get-Date",
                "$stepDuration = ($stepEnd - $stepStart).TotalSeconds",
                "",
                "$pipelineResults += @{",
                $"    Step = '{step.Name}'",
                "    ExitCode = $stepExitCode",
                "    Duration = $stepDuration",
                "    Output = $stepOutput",
                "    Timestamp = $stepStart",
                "}",
                "",
                $"Write-Host \"[PIPELINE: {step.Name}] Completed in $stepDuration seconds (Exit: $stepExitCode)\" -ForegroundColor $(if ($stepExitCode -eq 0) {{ 'Green' }} else {{ 'Red' }})",
                "Write-Host $stepOutput",
                ""
            });

            if (!step.ContinueOnError)
            {
                scriptLines.AddRange(new[]
                {
                    "if ($stepExitCode -ne 0) {",
                    $"    Write-Host \"[PIPELINE] Stopping due to error in step: {step.Name}\" -ForegroundColor Red",
                    "    $pipelineResults | ConvertTo-Json -Depth 10 | Out-File $pipelineLog",
                    "    exit $stepExitCode",
                    "}"
                });
            }
        }

        scriptLines.AddRange(new[]
        {
            "",
            "# Save pipeline results",
            "$pipelineResults | ConvertTo-Json -Depth 10 | Out-File $pipelineLog",
            "Write-Host \"`n[PIPELINE] All steps completed. Results saved to: $pipelineLog\" -ForegroundColor Green"
        });

        var script = string.Join("\n", scriptLines);

        return new VMJobSpec
        {
            VMSelector = new VMSelector
            {
                OS = "windows",
                NodeName = request.Advanced?.TargetVM,
                Tags = request.Advanced?.Tags
            },
            Command = "powershell.exe",
            Args = new List<string> { "-ExecutionPolicy", "Bypass", "-Command", script },
            WorkingDir = request.Advanced?.WorkingDir ?? "C:\\",
            Timeout = request.Advanced?.Timeout ?? "2h",
            Priority = request.Advanced?.Priority ?? 50
        };
    }

    private VMJobSpec ProcessTemplate(CreateVMJobRequest request)
    {
        if (!_templates.ContainsKey(request.Template!))
        {
            throw new ArgumentException($"Template '{request.Template}' not found");
        }

        var template = _templates[request.Template!];
        var commands = new List<string>();

        // Replace parameters in template commands
        foreach (var cmd in template.Commands)
        {
            var processedCmd = cmd;
            if (request.Parameters != null)
            {
                foreach (var param in request.Parameters)
                {
                    processedCmd = processedCmd.Replace($"{{{param.Key}}}", param.Value?.ToString() ?? "");
                }
            }
            commands.Add(processedCmd);
        }

        // Process as multi-command job
        var templateRequest = new CreateVMJobRequest
        {
            Name = request.Name,
            Commands = commands,
            Advanced = request.Advanced
        };

        return ProcessMultipleCommands(templateRequest);
    }

    private (string command, List<string> args) ParseCommand(string input)
    {
        var trimmedInput = input.Trim();
        
        // Check if user explicitly specified cmd.exe
        if (trimmedInput.StartsWith("cmd ", StringComparison.OrdinalIgnoreCase) || 
            trimmedInput.StartsWith("cmd.exe ", StringComparison.OrdinalIgnoreCase))
        {
            // Extract the command after cmd/cmd.exe
            var cmdMatch = Regex.Match(trimmedInput, @"^cmd(?:\.exe)?\s+(.+)", RegexOptions.IgnoreCase);
            if (cmdMatch.Success)
            {
                var cmdCommand = cmdMatch.Groups[1].Value.Trim();
                // Support both: "cmd echo test" and "cmd /c echo test"
                if (cmdCommand.StartsWith("/c ", StringComparison.OrdinalIgnoreCase))
                {
                    return ("cmd.exe", new List<string> { "/c", cmdCommand.Substring(3).Trim() });
                }
                return ("cmd.exe", new List<string> { "/c", cmdCommand });
            }
        }
        
        // Check if user explicitly specified pwsh.exe (PowerShell Core)
        if (trimmedInput.StartsWith("pwsh ", StringComparison.OrdinalIgnoreCase) || 
            trimmedInput.StartsWith("pwsh.exe ", StringComparison.OrdinalIgnoreCase))
        {
            var pwshMatch = Regex.Match(trimmedInput, @"^pwsh(?:\.exe)?\s+(.+)", RegexOptions.IgnoreCase);
            if (pwshMatch.Success)
            {
                var pwshCommand = pwshMatch.Groups[1].Value.Trim();
                return ("pwsh.exe", new List<string> { 
                    "-ExecutionPolicy", "Bypass", 
                    "-NoProfile",
                    "-Command", pwshCommand 
                });
            }
        }
        
        // Check if user explicitly specified powershell.exe
        if (trimmedInput.StartsWith("powershell ", StringComparison.OrdinalIgnoreCase) || 
            trimmedInput.StartsWith("powershell.exe ", StringComparison.OrdinalIgnoreCase))
        {
            var psMatch = Regex.Match(trimmedInput, @"^powershell(?:\.exe)?\s+(.+)", RegexOptions.IgnoreCase);
            if (psMatch.Success)
            {
                var psCommand = psMatch.Groups[1].Value.Trim();
                return ("powershell.exe", new List<string> { 
                    "-ExecutionPolicy", "Bypass", 
                    "-NoProfile",
                    "-Command", psCommand 
                });
            }
        }
        
        // Check for Python scripts (special case)
        if (trimmedInput.StartsWith("python ", StringComparison.OrdinalIgnoreCase) || 
            trimmedInput.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmedInput.Split(' ', 2);
            if (parts[0].Equals("python", StringComparison.OrdinalIgnoreCase))
            {
                return ("python.exe", parts.Length > 1 ? parts[1].Split(' ').ToList() : new List<string>());
            }
            return ("python.exe", trimmedInput.Split(' ').ToList());
        }
        
        // Check for direct executable calls (e.g., notepad.exe, calc.exe)
        if (Regex.IsMatch(trimmedInput, @"^[\w\-]+\.exe(?:\s+|$)", RegexOptions.IgnoreCase))
        {
            var parts = trimmedInput.Split(' ', 2);
            return (parts[0], parts.Length > 1 ? parts[1].Split(' ').ToList() : new List<string>());
        }
        
        // DEFAULT: Everything else runs in PowerShell
        // Clean up the command for PowerShell execution
        var cleanCommand = trimmedInput.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();
        
        return ("powershell.exe", new List<string> { 
            "-ExecutionPolicy", "Bypass", 
            "-NoProfile",
            "-Command", cleanCommand 
        });
    }

    private Dictionary<string, JobTemplate> LoadTemplates()
    {
        // TODO: Load from configuration or database
        return new Dictionary<string, JobTemplate>
        {
            ["health-check"] = new JobTemplate
            {
                Name = "health-check",
                Description = "System health check",
                Commands = new List<string>
                {
                    "Get-ComputerInfo | Select-Object CsName, OsName, OsVersion | ConvertTo-Json > {outputPath}\\info.json",
                    "Get-Service | Where-Object {$_.Status -eq 'Running'} | Select-Object Name, DisplayName | ConvertTo-Json > {outputPath}\\services.json",
                    "Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 | ConvertTo-Json > {outputPath}\\processes.json"
                },
                DefaultParameters = new Dictionary<string, string>
                {
                    ["outputPath"] = "C:\\vmjob-results"
                }
            },
            ["disk-cleanup"] = new JobTemplate
            {
                Name = "disk-cleanup",
                Description = "Clean temporary files",
                Commands = new List<string>
                {
                    "Remove-Item -Path 'C:\\Windows\\Temp\\*' -Recurse -Force -ErrorAction SilentlyContinue",
                    "Remove-Item -Path 'C:\\Users\\*\\AppData\\Local\\Temp\\*' -Recurse -Force -ErrorAction SilentlyContinue",
                    "Get-PSDrive C | Select-Object Used, Free | ConvertTo-Json > {outputPath}\\disk-stats.json"
                },
                DefaultParameters = new Dictionary<string, string>
                {
                    ["outputPath"] = "C:\\vmjob-results"
                }
            }
        };
    }

    private VMJobSpec ProcessStages(CreateVMJobRequest request)
    {
        // Convert stages to a single PowerShell script
        var script = BuildScriptFromStages(request);
        
        // Return a simple spec with the generated script
        var spec = new VMJobSpec
        {
            VMSelector = new VMSelector
            {
                OS = "windows",
                NodeName = request.Advanced?.TargetVM,
                Tags = request.Advanced?.Tags
            },
            Command = "powershell.exe",
            Args = new List<string> { "-ExecutionPolicy", "Bypass", "-Command", script },
            WorkingDir = request.Advanced?.WorkingDir ?? "C:\\",
            Timeout = request.Advanced?.Timeout ?? "2h", // Longer timeout for multi-stage
            Priority = request.Advanced?.Priority ?? 50
        };

        return spec;
    }
    
    private string BuildScriptFromStages(CreateVMJobRequest request)
    {
        var scriptBuilder = new System.Text.StringBuilder();
        var jobName = request.Name ?? "stage-job";
        var stages = request.Stages;
        var variables = request.Variables;
        var globalEnv = request.Environment;

        // Add header
        scriptBuilder.AppendLine("# Stage-based VMJob Execution Script");
        scriptBuilder.AppendLine($"# Job: {jobName}");
        scriptBuilder.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        scriptBuilder.AppendLine("");

        // Add variables
        scriptBuilder.AppendLine("# Variables");
        if (variables != null)
        {
            foreach (var kvp in variables)
            {
                scriptBuilder.AppendLine($"$var_{kvp.Key} = '{kvp.Value}'");
            }
        }
        scriptBuilder.AppendLine("");

        // Add global environment variables
        scriptBuilder.AppendLine("# Global Environment Variables");
        if (globalEnv != null)
        {
            foreach (var env in globalEnv)
            {
                var value = ReplaceVariables(env.Value, variables);
                scriptBuilder.AppendLine($"$env:{env.Name} = '{value}'");
            }
        }
        scriptBuilder.AppendLine("");

        // Add error handling
        scriptBuilder.AppendLine("# Error Handling");
        scriptBuilder.AppendLine("$ErrorActionPreference = 'Stop'");
        scriptBuilder.AppendLine("$stageResults = @{}");
        scriptBuilder.AppendLine("");

        // Process each stage
        int stageIndex = 0;
        foreach (var stage in stages)
        {
            stageIndex++;
            var stageName = stage.Name ?? $"stage{stageIndex}";
            var displayName = stage.DisplayName ?? stageName;

            scriptBuilder.AppendLine($"# Stage {stageIndex}: {displayName}");
            scriptBuilder.AppendLine("try {");
            scriptBuilder.AppendLine($"    Write-Host '===== Starting Stage: {displayName} =====' -ForegroundColor Cyan");

            // Stage environment variables
            if (stage.Environment != null)
            {
                foreach (var env in stage.Environment)
                {
                    var value = ReplaceVariables(env.Value, variables);
                    scriptBuilder.AppendLine($"    $env:{env.Name} = '{value}'");
                }
            }

            // Process stage steps
            int stepIndex = 0;
            foreach (var step in stage.Steps)
            {
                stepIndex++;
                var taskType = step.Task ?? "PowerShell";
                var stepName = step.DisplayName ?? $"Step {stepIndex}";
                var workingDir = step.WorkingDirectory;

                scriptBuilder.AppendLine($"    # {stepName}");
                scriptBuilder.AppendLine($"    Write-Host 'Executing: {stepName}' -ForegroundColor Yellow");

                // Set working directory if specified
                if (!string.IsNullOrEmpty(workingDir))
                {
                    var resolvedDir = ReplaceVariables(workingDir, variables);
                    scriptBuilder.AppendLine($"    Set-Location '{resolvedDir}'");
                }

                // Task environment variables
                if (step.Environment != null)
                {
                    foreach (var env in step.Environment)
                    {
                        var value = ReplaceVariables(env.Value, variables);
                        scriptBuilder.AppendLine($"    $env:{env.Name} = '{value}'");
                    }
                }

                // Execute the script
                if (step.Inputs?.Script != null)
                {
                    _logger.LogInformation($"Processing script for step {stepName}, type: {step.Inputs.Script.GetType()}");
                    
                    if (step.Inputs.Script is string scriptStr)
                    {
                        var resolvedScript = ReplaceVariables(scriptStr, variables);
                        scriptBuilder.AppendLine($"    {resolvedScript}");
                    }
                    else if (step.Inputs.Script is System.Text.Json.JsonElement jsonElement)
                    {
                        // Handle JsonElement from System.Text.Json deserialization
                        if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var scriptString = jsonElement.GetString();
                            var resolvedScript = ReplaceVariables(scriptString ?? "", variables);
                            scriptBuilder.AppendLine($"    {resolvedScript}");
                        }
                        else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var item in jsonElement.EnumerateArray())
                            {
                                var line = item.GetString() ?? "";
                                var resolvedLine = ReplaceVariables(line, variables);
                                scriptBuilder.AppendLine($"    {resolvedLine}");
                            }
                        }
                    }
                    else if (step.Inputs.Script is List<string> scriptArray)
                    {
                        foreach (var line in scriptArray)
                        {
                            var resolvedLine = ReplaceVariables(line, variables);
                            scriptBuilder.AppendLine($"    {resolvedLine}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Unknown script type: {step.Inputs.Script.GetType()}");
                    }
                }
                else
                {
                    _logger.LogInformation($"No script found for step {stepName}");
                }
            }

            scriptBuilder.AppendLine();
            scriptBuilder.AppendLine($"    Write-Host '===== Completed Stage: {displayName} =====' -ForegroundColor Green");
            scriptBuilder.AppendLine($"    $stageResults['{stageName}'] = 'Success'");
            scriptBuilder.AppendLine("} catch {");
            scriptBuilder.AppendLine($"    Write-Host '===== Failed Stage: {displayName} =====' -ForegroundColor Red");
            scriptBuilder.AppendLine($"    Write-Host \"Error: $_\" -ForegroundColor Red");
            scriptBuilder.AppendLine($"    $stageResults['{stageName}'] = 'Failed'");
            scriptBuilder.AppendLine("    throw");
            scriptBuilder.AppendLine("}");
        }

        // Add finally block if present
        if (request.Finally?.Steps != null)
        {
            scriptBuilder.AppendLine("# Finally Block");
            scriptBuilder.AppendLine("try {");
            scriptBuilder.AppendLine("    Write-Host '===== Finally Block =====' -ForegroundColor Magenta");

            foreach (var step in request.Finally.Steps)
            {
                if (step.Inputs?.Script != null)
                {
                    if (step.Inputs.Script is string scriptStr)
                    {
                        scriptBuilder.AppendLine($"    {scriptStr}");
                    }
                    else if (step.Inputs.Script is List<string> scriptArray)
                    {
                        foreach (var line in scriptArray)
                        {
                            scriptBuilder.AppendLine($"    {line}");
                        }
                    }
                }
            }

            scriptBuilder.AppendLine("} catch {");
            scriptBuilder.AppendLine("    Write-Host 'Finally block error (non-fatal)' -ForegroundColor Yellow");
            scriptBuilder.AppendLine("}");
        }

        // Add summary
        scriptBuilder.AppendLine("# Summary");
        scriptBuilder.AppendLine("Write-Host '===== Job Summary =====' -ForegroundColor Cyan");
        scriptBuilder.AppendLine("$stageResults.GetEnumerator() | ForEach-Object {");
        scriptBuilder.AppendLine("    Write-Host \"Stage $($_.Key): $($_.Value)\" -ForegroundColor $(if($_.Value -eq 'Success'){'Green'}else{'Red'})");
        scriptBuilder.AppendLine("}");

        return scriptBuilder.ToString();
    }
    
    private string ReplaceVariables(string input, Dictionary<string, object> variables)
    {
        if (variables == null || string.IsNullOrEmpty(input))
            return input;

        var result = input;
        foreach (var kvp in variables)
        {
            var key = $"$(variables.{kvp.Key})";
            var value = kvp.Value?.ToString() ?? "";
            result = result.Replace(key, value);
        }

        return result;
    }
}
