using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using VMJobAPI.Services;
using VMJobAPI.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace VMJobAPI.Tests
{
    public class GenerateExpectedFiles
    {
        private readonly JobRequestProcessor _processor;
        private readonly Mock<ILogger<JobRequestProcessor>> _loggerMock;

        public GenerateExpectedFiles()
        {
            _loggerMock = new Mock<ILogger<JobRequestProcessor>>();
            _processor = new JobRequestProcessor(_loggerMock.Object);
        }

        [Fact(Skip = "Run this manually to regenerate expected files")]
        public void GenerateAllExpectedFiles()
        {
            GenerateSimpleStageExpected();
            GenerateEnvironmentVariablesExpected();
            GenerateWorkingDirectoryExpected();
            GenerateFinallyBlockExpected();
            GenerateFileCreationExpected();
        }

        private void GenerateSimpleStageExpected()
        {
            var request = new CreateVMJobRequest
            {
                Name = "stage-job",
                Variables = new Dictionary<string, object>
                {
                    { "testVar", "TestValue" }
                },
                Stages = new List<VMJobStage>
                {
                    new VMJobStage
                    {
                        Name = "build",
                        DisplayName = "Build Stage",
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                DisplayName = "Echo test",
                                Inputs = new TaskInputs
                                {
                                    Script = "Write-Host 'Building with $(variables.testVar)'"
                                }
                            }
                        }
                    }
                }
            };

            var spec = _processor.ProcessRequest(request);
            var script = ScriptComparisonHelper.NormalizeScript(spec.Args[spec.Args.Count - 1]);
            File.WriteAllText("TestScripts/SimpleStageExpected.ps1", script);
        }

        private void GenerateEnvironmentVariablesExpected()
        {
            var request = new CreateVMJobRequest
            {
                Name = "env-stage-job",
                Environment = new List<EnvironmentVariable>
                {
                    new EnvironmentVariable { Name = "GLOBAL_VAR", Value = "global_value" }
                },
                Stages = new List<VMJobStage>
                {
                    new VMJobStage
                    {
                        Name = "test",
                        Environment = new List<EnvironmentVariable>
                        {
                            new EnvironmentVariable { Name = "STAGE_VAR", Value = "stage_value" }
                        },
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                Environment = new List<EnvironmentVariable>
                                {
                                    new EnvironmentVariable { Name = "TASK_VAR", Value = "task_value" }
                                },
                                Inputs = new TaskInputs
                                {
                                    Script = "Write-Host \"$env:GLOBAL_VAR, $env:STAGE_VAR, $env:TASK_VAR\""
                                }
                            }
                        }
                    }
                }
            };

            var spec = _processor.ProcessRequest(request);
            var script = ScriptComparisonHelper.NormalizeScript(spec.Args[spec.Args.Count - 1]);
            File.WriteAllText("TestScripts/EnvironmentVariablesExpected.ps1", script);
        }

        private void GenerateWorkingDirectoryExpected()
        {
            var request = new CreateVMJobRequest
            {
                Name = "workdir-job",
                Stages = new List<VMJobStage>
                {
                    new VMJobStage
                    {
                        Name = "test",
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                WorkingDirectory = "C:\\custom\\path",
                                Inputs = new TaskInputs
                                {
                                    Script = "Get-Location"
                                }
                            }
                        }
                    }
                }
            };

            var spec = _processor.ProcessRequest(request);
            var script = ScriptComparisonHelper.NormalizeScript(spec.Args[spec.Args.Count - 1]);
            File.WriteAllText("TestScripts/WorkingDirectoryExpected.ps1", script);
        }

        private void GenerateFinallyBlockExpected()
        {
            var request = new CreateVMJobRequest
            {
                Name = "finally-job",
                Stages = new List<VMJobStage>
                {
                    new VMJobStage
                    {
                        Name = "test",
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                Inputs = new TaskInputs { Script = "Write-Host 'Main task'" }
                            }
                        }
                    }
                },
                Finally = new FinallyBlock
                {
                    Steps = new List<VMJobTask>
                    {
                        new VMJobTask
                        {
                            Task = "PowerShell",
                            Inputs = new TaskInputs { Script = "Write-Host 'Cleanup task'" }
                        }
                    }
                }
            };

            var spec = _processor.ProcessRequest(request);
            var script = ScriptComparisonHelper.NormalizeScript(spec.Args[spec.Args.Count - 1]);
            File.WriteAllText("TestScripts/FinallyBlockExpected.ps1", script);
        }

        private void GenerateFileCreationExpected()
        {
            var request = new CreateVMJobRequest
            {
                Name = "file-creation-job",
                Variables = new Dictionary<string, object>
                {
                    { "outputPath", "C:\\vmjob-results" },
                    { "fileName", "test-output.txt" }
                },
                Stages = new List<VMJobStage>
                {
                    new VMJobStage
                    {
                        Name = "setup",
                        DisplayName = "Setup Directory",
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                DisplayName = "Create directory",
                                Inputs = new TaskInputs
                                {
                                    Script = @"
if (!(Test-Path '$(variables.outputPath)')) {
    New-Item -ItemType Directory -Path '$(variables.outputPath)' -Force | Out-Null
    Write-Host 'Created directory: $(variables.outputPath)'
}"
                                }
                            }
                        }
                    },
                    new VMJobStage
                    {
                        Name = "create-file",
                        DisplayName = "Create File",
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                DisplayName = "Write file",
                                WorkingDirectory = "$(variables.outputPath)",
                                Inputs = new TaskInputs
                                {
                                    Script = @"
$content = @'
File created by VMJob API test
Date: $(Get-Date)
Path: $(variables.outputPath)\$(variables.fileName)
'@
$content | Out-File -FilePath '$(variables.fileName)' -Force
Write-Host 'File created successfully'"
                                }
                            }
                        }
                    }
                }
            };

            var spec = _processor.ProcessRequest(request);
            var script = ScriptComparisonHelper.NormalizeScript(spec.Args[spec.Args.Count - 1]);
            File.WriteAllText("TestScripts/FileCreationExpected.ps1", script);
        }
    }
}
