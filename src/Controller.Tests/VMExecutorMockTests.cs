using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VMJobOrchestrator;
using Xunit;

namespace VMJobOrchestrator.Tests
{
    public class VMExecutorMockTests
    {
        private readonly Mock<IVMExecutor> _mockVMExecutor;
        private readonly Mock<ILogger<VMExecutor>> _mockLogger;

        public VMExecutorMockTests()
        {
            _mockVMExecutor = new Mock<IVMExecutor>();
            _mockLogger = new Mock<ILogger<VMExecutor>>();
        }

        [Fact]
        public async Task ExecuteRemoteAsync_Success_ShouldReturnCorrectResult()
        {
            // Arrange
            var expectedResult = new ExecutionResult
            {
                Success = true,
                ExitCode = 0,
                StandardOutput = "Job executed successfully",
                StandardError = "",
                ResultPath = @"C:\vmjob-results\test-job.txt",
                ExecutionTime = TimeSpan.FromMilliseconds(5000)
            };

            _mockVMExecutor.Setup(x => x.ExecuteRemoteAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<List<EnvVar>>(),
                It.IsAny<TimeSpan>()))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _mockVMExecutor.Object.ExecuteRemoteAsync(
                "192.168.1.10",
                5985,
                "powershell.exe",
                new List<string> { "-Command", "Write-Host 'Test'" },
                @"C:\vmjob-results",
                new List<EnvVar>(),
                TimeSpan.FromMinutes(5));

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.ExitCode.Should().Be(0);
            result.StandardOutput.Should().Be("Job executed successfully");
            result.ExecutionTime.Should().Be(TimeSpan.FromMilliseconds(5000));

            _mockVMExecutor.Verify(x => x.ExecuteRemoteAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<List<EnvVar>>(),
                It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteRemoteAsync_Failure_ShouldReturnErrorResult()
        {
            // Arrange
            var expectedResult = new ExecutionResult
            {
                Success = false,
                ExitCode = -1,
                StandardOutput = "",
                StandardError = "Command failed: Access denied",
                ResultPath = "",
                ExecutionTime = TimeSpan.FromMilliseconds(1000)
            };

            _mockVMExecutor.Setup(x => x.ExecuteRemoteAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<List<EnvVar>>(),
                It.IsAny<TimeSpan>()))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _mockVMExecutor.Object.ExecuteRemoteAsync(
                "192.168.1.10",
                5985,
                "powershell.exe",
                new List<string> { "-Command", "Test-Path 'C:\\restricted'" },
                @"C:\vmjob-results",
                new List<EnvVar>(),
                TimeSpan.FromMinutes(1));

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ExitCode.Should().Be(-1);
            result.StandardError.Should().Contain("Access denied");
            result.ExecutionTime.Should().Be(TimeSpan.FromMilliseconds(1000));
        }

        [Fact]
        public async Task ExecuteRemoteAsync_Timeout_ShouldReturnTimeoutError()
        {
            // Arrange
            _mockVMExecutor.Setup(x => x.ExecuteRemoteAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<List<EnvVar>>(),
                It.IsAny<TimeSpan>()))
                .ThrowsAsync(new TimeoutException("Operation timed out after 30 seconds"));

            // Act & Assert
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await _mockVMExecutor.Object.ExecuteRemoteAsync(
                    "192.168.1.10",
                    5985,
                    "powershell.exe",
                    new List<string> { "-Command", "Start-Sleep -Seconds 60" },
                    @"C:\vmjob-results",
                    new List<EnvVar>(),
                    TimeSpan.FromSeconds(30));
            });
        }

        [Fact]
        public async Task ExecuteRemoteAsync_WithEnvironmentVariables_ShouldPassCorrectly()
        {
            // Arrange
            var envVars = new List<EnvVar>
            {
                new EnvVar { Name = "TEST_VAR", Value = "test_value" },
                new EnvVar { Name = "ANOTHER_VAR", Value = "another_value" }
            };

            List<EnvVar> capturedEnvVars = null;

            _mockVMExecutor.Setup(x => x.ExecuteRemoteAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<List<EnvVar>>(),
                It.IsAny<TimeSpan>()))
                .Callback<string, int, string, List<string>, string, List<EnvVar>, TimeSpan>(
                    (host, port, cmd, args, wd, env, timeout) => capturedEnvVars = env)
                .ReturnsAsync(new ExecutionResult { Success = true, ExitCode = 0 });

            // Act
            await _mockVMExecutor.Object.ExecuteRemoteAsync(
                "192.168.1.10",
                5985,
                "powershell.exe",
                new List<string> { "-Command", "echo $env:TEST_VAR" },
                @"C:\vmjob-results",
                envVars,
                TimeSpan.FromMinutes(1));

            // Assert
            capturedEnvVars.Should().NotBeNull();
            capturedEnvVars.Should().HaveCount(2);
            capturedEnvVars[0].Name.Should().Be("TEST_VAR");
            capturedEnvVars[0].Value.Should().Be("test_value");
            capturedEnvVars[1].Name.Should().Be("ANOTHER_VAR");
            capturedEnvVars[1].Value.Should().Be("another_value");
        }

        [Theory]
        [InlineData(30000, 30)]  // 30 seconds
        [InlineData(90000, 90)]  // 1.5 minutes
        [InlineData(300000, 300)] // 5 minutes
        public async Task ExecuteRemoteAsync_VariousDurations_ShouldTrackCorrectly(long executionTimeMs, int expectedSeconds)
        {
            // Arrange
            var expectedResult = new ExecutionResult
            {
                Success = true,
                ExitCode = 0,
                StandardOutput = $"Job completed in {executionTimeMs}ms",
                StandardError = "",
                ExecutionTime = TimeSpan.FromMilliseconds(executionTimeMs)
            };

            _mockVMExecutor.Setup(x => x.ExecuteRemoteAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<List<EnvVar>>(),
                It.IsAny<TimeSpan>()))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _mockVMExecutor.Object.ExecuteRemoteAsync(
                "192.168.1.10",
                5985,
                "powershell.exe",
                new List<string> { "-Command", $"Start-Sleep -Seconds {expectedSeconds}" },
                @"C:\vmjob-results",
                new List<EnvVar>(),
                TimeSpan.FromMinutes(10));

            // Assert
            result.ExecutionTime.Should().Be(TimeSpan.FromMilliseconds(executionTimeMs));
            var executionSeconds = result.ExecutionTime.TotalSeconds;
            executionSeconds.Should().Be(expectedSeconds);
        }
    }
}
