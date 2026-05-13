using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FluentAssertions;
using VMJobAPI.Services;
using VMJobAPI.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace VMJobAPI.Tests
{
    public class ScriptGenerationTests
    {
        private readonly JobRequestProcessor _processor;
        private readonly Mock<ILogger<JobRequestProcessor>> _loggerMock;

        public ScriptGenerationTests()
        {
            _loggerMock = new Mock<ILogger<JobRequestProcessor>>();
            _processor = new JobRequestProcessor(_loggerMock.Object);
        }

        [Fact]
        public void GeneratedScript_ForFileCreation_WorksCorrectly()
        {
            // Arrange
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

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            var actualScript = spec.Args.Last();
            var expectedScript = ScriptComparisonHelper.LoadExpectedScript("FileCreationExpected.ps1");
            
            var (isEqual, differences) = ScriptComparisonHelper.CompareScripts(actualScript, expectedScript);
            isEqual.Should().BeTrue(differences);
        }

        [Fact]
        public void GeneratedScript_ComplexWorkflow_IncludesAllComponents()
        {
            // Arrange
            var request = new CreateVMJobRequest
            {
                Name = "complex-workflow",
                Variables = new Dictionary<string, object>
                {
                    { "projectName", "TestApp" },
                    { "buildConfig", "Release" },
                    { "outputDir", "C:\\build\\output" }
                },
                Environment = new List<EnvironmentVariable>
                {
                    new EnvironmentVariable { Name = "BUILD_NUMBER", Value = "123" },
                    new EnvironmentVariable { Name = "BRANCH", Value = "main" }
                },
                Stages = new List<VMJobStage>
                {
                    new VMJobStage
                    {
                        Name = "prepare",
                        DisplayName = "Prepare Environment",
                        Environment = new List<EnvironmentVariable>
                        {
                            new EnvironmentVariable { Name = "STAGE", Value = "prepare" }
                        },
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                DisplayName = "Clean output directory",
                                Inputs = new TaskInputs
                                {
                                    Script = @"
if (Test-Path '$(variables.outputDir)') {
    Remove-Item '$(variables.outputDir)' -Recurse -Force
}
New-Item -ItemType Directory -Path '$(variables.outputDir)' -Force"
                                }
                            }
                        }
                    },
                    new VMJobStage
                    {
                        Name = "build",
                        DisplayName = "Build Application",
                        Environment = new List<EnvironmentVariable>
                        {
                            new EnvironmentVariable { Name = "STAGE", Value = "build" }
                        },
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                DisplayName = "Build project",
                                Inputs = new TaskInputs
                                {
                                    Script = @"
Write-Host ""Building $(variables.projectName) in $(variables.buildConfig) mode""
Write-Host ""Build number: $env:BUILD_NUMBER""
Write-Host ""Branch: $env:BRANCH""
Write-Host ""Stage: $env:STAGE"""
                                }
                            },
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                DisplayName = "Copy outputs",
                                WorkingDirectory = "$(variables.outputDir)",
                                Inputs = new TaskInputs
                                {
                                    Script = "'Build completed' | Out-File -FilePath 'build.log'"
                                }
                            }
                        }
                    },
                    new VMJobStage
                    {
                        Name = "test",
                        DisplayName = "Run Tests",
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                DisplayName = "Run unit tests",
                                Inputs = new TaskInputs
                                {
                                    Script = "Write-Host 'Running tests for $(variables.projectName)'"
                                }
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
                            Inputs = new TaskInputs
                            {
                                Script = @"
Write-Host 'Workflow completed'
Write-Host ""Total stages: $($stageResults.Count)""
Write-Host ""Successful stages: $(($stageResults.Values | Where-Object { $_ -eq 'Success' }).Count)"""
                            }
                        }
                    }
                }
            };

            // Act
            var spec = _processor.ProcessRequest(request);
            var script = spec.Args.Last();

            // Assert
            // Check variables
            script.Should().Contain("$var_projectName = 'TestApp'");
            script.Should().Contain("$var_buildConfig = 'Release'");
            script.Should().Contain("$var_outputDir = 'C:\\build\\output'");

            // Check global environment
            script.Should().Contain("$env:BUILD_NUMBER = '123'");
            script.Should().Contain("$env:BRANCH = 'main'");

            // Check stage environment
            script.Should().Contain("$env:STAGE = 'prepare'");
            script.Should().Contain("$env:STAGE = 'build'");

            // Check all stages
            script.Should().Contain("# Stage 1: Prepare Environment");
            script.Should().Contain("# Stage 2: Build Application");
            script.Should().Contain("# Stage 3: Run Tests");

            // Check working directory change
            script.Should().Contain("Set-Location 'C:\\build\\output'");

            // Check variable substitution
            script.Should().Contain("Building TestApp in Release mode");
            script.Should().Contain("Running tests for TestApp");

            // Check finally block
            script.Should().Contain("# Finally Block");
            script.Should().Contain("Workflow completed");

            // Check error handling
            script.Should().Contain("$ErrorActionPreference = 'Stop'");
            script.Should().Contain("$stageResults = @{}");
            script.Should().Contain("$stageResults['prepare'] = 'Success'");
            script.Should().Contain("$stageResults['build'] = 'Success'");
            script.Should().Contain("$stageResults['test'] = 'Success'");
        }

        [Fact]
        public void GeneratedScript_WithSpecialCharacters_EscapesCorrectly()
        {
            // Arrange
            var request = new CreateVMJobRequest
            {
                Name = "special-chars-job",
                Variables = new Dictionary<string, object>
                {
                    { "message", "Test with 'quotes' and \"double quotes\"" },
                    { "path", "C:\\Program Files\\My App" }
                },
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
                                Inputs = new TaskInputs
                                {
                                    Script = "Write-Host '$(variables.message)' | Out-File -FilePath '$(variables.path)\\output.txt'"
                                }
                            }
                        }
                    }
                }
            };

            // Act
            var spec = _processor.ProcessRequest(request);
            var script = spec.Args.Last();

            // Assert
            script.Should().Contain("$var_message = 'Test with 'quotes' and \"double quotes\"'");
            script.Should().Contain("$var_path = 'C:\\Program Files\\My App'");
            script.Should().Contain("Write-Host 'Test with 'quotes' and \"double quotes\"'");
        }

        [Theory]
        [InlineData("echo test", "powershell.exe", new[] { "-ExecutionPolicy", "Bypass", "-NoProfile", "-Command", "echo test" })]
        [InlineData("cmd /c dir", "cmd.exe", new[] { "/c", "dir" })]
        [InlineData("pwsh -Command Get-Process", "pwsh.exe", new[] { "-ExecutionPolicy", "Bypass", "-NoProfile", "-Command", "-Command Get-Process" })]
        [InlineData("python script.py", "python.exe", new[] { "script.py" })]
        public void ProcessRequest_DifferentCommandTypes_ParsesCorrectly(string command, string expectedExe, string[] expectedArgs)
        {
            // Arrange
            var request = new CreateVMJobRequest
            {
                Name = "parse-test",
                Command = command
            };

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            spec.Command.Should().Be(expectedExe);
            spec.Args.Should().BeEquivalentTo(expectedArgs);
        }

        [Fact]
        public void GeneratedScript_Summary_ShowsAllStageResults()
        {
            // Arrange
            var request = new CreateVMJobRequest
            {
                Name = "summary-test",
                Stages = new List<VMJobStage>
                {
                    new VMJobStage { Name = "stage1", Steps = new List<VMJobTask> { new VMJobTask { Task = "PowerShell", Inputs = new TaskInputs { Script = "Write-Host 'Stage 1'" } } } },
                    new VMJobStage { Name = "stage2", Steps = new List<VMJobTask> { new VMJobTask { Task = "PowerShell", Inputs = new TaskInputs { Script = "Write-Host 'Stage 2'" } } } },
                    new VMJobStage { Name = "stage3", Steps = new List<VMJobTask> { new VMJobTask { Task = "PowerShell", Inputs = new TaskInputs { Script = "Write-Host 'Stage 3'" } } } }
                }
            };

            // Act
            var spec = _processor.ProcessRequest(request);
            var script = spec.Args.Last();

            // Assert
            script.Should().Contain("# Summary");
            script.Should().Contain("===== Job Summary =====");
            script.Should().Contain("$stageResults.GetEnumerator() | ForEach-Object");
            script.Should().Contain("Write-Host \"Stage $($_.Key): $($_.Value)\"");
        }
    }
}
