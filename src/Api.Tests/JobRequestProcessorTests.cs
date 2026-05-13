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
    public class JobRequestProcessorTests
    {
        private readonly JobRequestProcessor _processor;
        private readonly Mock<ILogger<JobRequestProcessor>> _loggerMock;

        public JobRequestProcessorTests()
        {
            _loggerMock = new Mock<ILogger<JobRequestProcessor>>();
            _processor = new JobRequestProcessor(_loggerMock.Object);
        }

        [Fact]
        public void ProcessRequest_SimpleCommand_ReturnsCorrectSpec()
        {
            // Arrange
            var request = new CreateVMJobRequest
            {
                Name = "test-job",
                Command = "echo 'Hello World'"
            };

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            spec.Should().NotBeNull();
            spec.Command.Should().Be("powershell.exe");
            spec.Args.Should().Contain("-Command");
            spec.Args.Should().Contain("echo 'Hello World'");
        }

        [Fact]
        public void ProcessRequest_MultipleCommands_CombinesIntoScript()
        {
            // Arrange
            var request = new CreateVMJobRequest
            {
                Name = "multi-command-job",
                Commands = new List<string>
                {
                    "mkdir C:\\test",
                    "echo 'test' > C:\\test\\file.txt",
                    "dir C:\\test"
                }
            };

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            spec.Should().NotBeNull();
            spec.Command.Should().Be("powershell.exe");
            spec.Args.Last().Should().Contain("mkdir C:\\test");
            spec.Args.Last().Should().Contain("echo 'test' > C:\\test\\file.txt");
            spec.Args.Last().Should().Contain("dir C:\\test");
        }

        [Fact]
        public void ProcessRequest_SimpleStage_GeneratesCorrectScript()
        {
            // Arrange
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

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            spec.Should().NotBeNull();
            spec.Command.Should().Be("powershell.exe");
            
            var actualScript = spec.Args.Last();
            var expectedScript = ScriptComparisonHelper.LoadExpectedScript("SimpleStageExpected.ps1");
            
            var (isEqual, differences) = ScriptComparisonHelper.CompareScripts(actualScript, expectedScript);
            isEqual.Should().BeTrue(differences);
        }

        [Fact]
        public void ProcessRequest_StageWithEnvironmentVariables_SetsCorrectly()
        {
            // Arrange
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

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            var actualScript = spec.Args.Last();
            var expectedScript = ScriptComparisonHelper.LoadExpectedScript("EnvironmentVariablesExpected.ps1");
            
            var (isEqual, differences) = ScriptComparisonHelper.CompareScripts(actualScript, expectedScript);
            isEqual.Should().BeTrue(differences);
        }

        [Fact]
        public void ProcessRequest_StageWithWorkingDirectory_ChangesDirectory()
        {
            // Arrange
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

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            var actualScript = spec.Args.Last();
            var expectedScript = ScriptComparisonHelper.LoadExpectedScript("WorkingDirectoryExpected.ps1");
            
            var (isEqual, differences) = ScriptComparisonHelper.CompareScripts(actualScript, expectedScript);
            isEqual.Should().BeTrue(differences);
        }

        [Fact]
        public void ProcessRequest_StageWithFinally_IncludesFinallyBlock()
        {
            // Arrange
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

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            var actualScript = spec.Args.Last();
            var expectedScript = ScriptComparisonHelper.LoadExpectedScript("FinallyBlockExpected.ps1");
            
            var (isEqual, differences) = ScriptComparisonHelper.CompareScripts(actualScript, expectedScript);
            isEqual.Should().BeTrue(differences);
        }

        [Fact]
        public void ProcessRequest_VariableReplacement_WorksCorrectly()
        {
            // Arrange
            var request = new CreateVMJobRequest
            {
                Name = "var-replacement-job",
                Variables = new Dictionary<string, object>
                {
                    { "projectName", "MyProject" },
                    { "version", "1.0.0" }
                },
                Stages = new List<VMJobStage>
                {
                    new VMJobStage
                    {
                        Name = "build",
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                Inputs = new TaskInputs
                                {
                                    Script = "Write-Host 'Building $(variables.projectName) version $(variables.version)'"
                                }
                            }
                        }
                    }
                }
            };

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            var script = spec.Args.Last();
            script.Should().Contain("$var_projectName = 'MyProject'");
            script.Should().Contain("$var_version = '1.0.0'");
            script.Should().Contain("Write-Host 'Building MyProject version 1.0.0'");
        }

        [Fact]
        public void ProcessRequest_MultipleStages_GeneratesAllStages()
        {
            // Arrange
            var request = new CreateVMJobRequest
            {
                Name = "multi-stage-job",
                Stages = new List<VMJobStage>
                {
                    new VMJobStage
                    {
                        Name = "build",
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                Inputs = new TaskInputs { Script = "Write-Host 'Building'" }
                            }
                        }
                    },
                    new VMJobStage
                    {
                        Name = "test",
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                Inputs = new TaskInputs { Script = "Write-Host 'Testing'" }
                            }
                        }
                    },
                    new VMJobStage
                    {
                        Name = "deploy",
                        Steps = new List<VMJobTask>
                        {
                            new VMJobTask
                            {
                                Task = "PowerShell",
                                Inputs = new TaskInputs { Script = "Write-Host 'Deploying'" }
                            }
                        }
                    }
                }
            };

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            var script = spec.Args.Last();
            script.Should().Contain("# Stage 1: build");
            script.Should().Contain("# Stage 2: test");
            script.Should().Contain("# Stage 3: deploy");
            script.Should().Contain("Write-Host 'Building'");
            script.Should().Contain("Write-Host 'Testing'");
            script.Should().Contain("Write-Host 'Deploying'");
        }

        [Fact]
        public void ProcessRequest_StageWithArrayScript_HandlesCorrectly()
        {
            // Arrange
            var request = new CreateVMJobRequest
            {
                Name = "array-script-job",
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
                                    Script = new List<string>
                                    {
                                        "$items = @(1, 2, 3)",
                                        "foreach ($item in $items) {",
                                        "    Write-Host \"Item: $item\"",
                                        "}"
                                    }
                                }
                            }
                        }
                    }
                }
            };

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            var script = spec.Args.Last();
            script.Should().Contain("$items = @(1, 2, 3)");
            script.Should().Contain("foreach ($item in $items) {");
            script.Should().Contain("Write-Host \"Item: $item\"");
        }

        [Fact]
        public void ProcessRequest_ErrorHandling_IncludesTryCatch()
        {
            // Arrange
            var request = new CreateVMJobRequest
            {
                Name = "error-handling-job",
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
                                Inputs = new TaskInputs { Script = "throw 'Test error'" }
                            }
                        }
                    }
                }
            };

            // Act
            var spec = _processor.ProcessRequest(request);

            // Assert
            var script = spec.Args.Last();
            script.Should().Contain("try {");
            script.Should().Contain("} catch {");
            script.Should().Contain("===== Failed Stage:");
            script.Should().Contain("$stageResults['test'] = 'Failed'");
            script.Should().Contain("throw");
        }
    }
}
