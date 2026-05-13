using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using VMJobOrchestrator;
using Xunit;

namespace VMJobOrchestrator.Tests
{
    public class ControllerIntegrationTests
    {
        private readonly Mock<IKubernetes> _mockKubernetesClient;
        private readonly Mock<ILogger<KubernetesStyleController>> _mockLogger;
        private readonly Mock<IVMExecutor> _mockVMExecutor;
        private readonly Mock<ICustomObjectsOperations> _mockCustomObjects;
        private readonly KubernetesStyleController _controller;

        public ControllerIntegrationTests()
        {
            _mockKubernetesClient = new Mock<IKubernetes>();
            _mockLogger = new Mock<ILogger<KubernetesStyleController>>();
            _mockVMExecutor = new Mock<IVMExecutor>();
            _mockCustomObjects = new Mock<ICustomObjectsOperations>();

            _mockKubernetesClient.Setup(x => x.CustomObjects).Returns(_mockCustomObjects.Object);

            _controller = new KubernetesStyleController(
                _mockKubernetesClient.Object,
                _mockLogger.Object,
                _mockVMExecutor.Object);
        }

        [Fact]
        public async Task FullJobExecution_ShouldTrackDurationCorrectly()
        {
            // Arrange
            var jobName = "integration-test-job";
            var capturedPatches = new List<string>();
            DateTime? capturedStartTime = null;
            DateTime? capturedCompletionTime = null;

            // Mock job listing
            var pendingJob = new
            {
                metadata = new { name = jobName },
                spec = new
                {
                    command = "powershell.exe",
                    args = new[] { "-Command", "Start-Sleep -Seconds 5; Write-Host 'Done'" },
                    workingDir = @"C:\vmjob-results",
                    timeout = "1m"
                },
                status = new { phase = "Pending" }
            };

            var jobList = new
            {
                items = new[] { pendingJob }
            };

            _mockCustomObjects.Setup(x => x.ListNamespacedCustomObjectAsync(
                "orchestrator.vmjobs.io", "v1", "default", "vmjobs"))
                .ReturnsAsync(jobList);

            // Mock VM node listing
            var vmNode = new
            {
                metadata = new { name = "test-vm-1" },
                spec = new
                {
                    privateIP = "192.168.1.10",
                    os = "windows",
                    status = "Ready"
                },
                status = new { isReady = true }
            };

            var vmNodeList = new
            {
                items = new[] { vmNode }
            };

            _mockCustomObjects.Setup(x => x.ListClusterCustomObjectAsync(
                "orchestrator.vmjobs.io", "v1", "vmnodes"))
                .ReturnsAsync(vmNodeList);

            // Mock getting the current job (for UpdateJobStatusAsync)
            _mockCustomObjects.Setup(x => x.GetNamespacedCustomObjectAsync(
                "orchestrator.vmjobs.io", "v1", "default",
                "vmjobs", jobName))
                .ReturnsAsync(() =>
                {
                    // Return different states based on how many patches we've received
                    if (capturedPatches.Count == 0)
                        return new { status = new { } };
                    else
                        return new { status = new { startTime = capturedStartTime?.ToString("o") } };
                });

            // Mock patching job status
            _mockCustomObjects.Setup(x => x.PatchNamespacedCustomObjectStatusAsync(
                It.IsAny<V1Patch>(),
                "orchestrator.vmjobs.io", "v1", "default",
                jobName, "vmjobs"))
                .Callback<V1Patch, string, string, string, string, string>(
                    (patch, group, version, ns, name, plural) =>
                    {
                        capturedPatches.Add(patch.Content);
                        dynamic patchObj = JsonConvert.DeserializeObject(patch.Content);
                        
                        if (patchObj.status.startTime != null)
                            capturedStartTime = DateTime.Parse((string)patchObj.status.startTime);
                        if (patchObj.status.completionTime != null)
                            capturedCompletionTime = DateTime.Parse((string)patchObj.status.completionTime);
                    })
                .ReturnsAsync(new object());

            // Mock VM execution
            _mockVMExecutor.Setup(x => x.ExecuteRemoteAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<List<EnvVar>>(),
                It.IsAny<TimeSpan>()))
                .ReturnsAsync(new ExecutionResult
                {
                    Success = true,
                    ExitCode = 0,
                    StandardOutput = "Done",
                    StandardError = "",
                    ExecutionTime = TimeSpan.FromMilliseconds(5000)
                });

            // Act - Start the controller for a brief period
            var cts = new CancellationTokenSource();
            var controllerTask = _controller.StartAsync(cts.Token);

            // Wait for the job to be processed
            await Task.Delay(3000);
            cts.Cancel();

            try
            {
                await controllerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }

            // Assert
            capturedPatches.Should().HaveCountGreaterThan(0);
            
            // Check that we set Running status with startTime
            var runningPatch = capturedPatches.FirstOrDefault(p => p.Contains("\"phase\":\"Running\""));
            runningPatch.Should().NotBeNull();
            runningPatch.Should().Contain("startTime");
            
            // Check that we set Completed status with duration
            var completedPatch = capturedPatches.FirstOrDefault(p => p.Contains("\"phase\":\"Completed\""));
            if (completedPatch != null)
            {
                completedPatch.Should().Contain("completionTime");
                completedPatch.Should().Contain("duration");
                completedPatch.Should().Contain("durationSeconds");
                
                dynamic completedPatchObj = JsonConvert.DeserializeObject(completedPatch);
                ((string)completedPatchObj.status.duration).Should().NotBeNullOrEmpty();
                ((int)completedPatchObj.status.durationSeconds).Should().BeGreaterThan(0);
            }

            // Verify proper logging
            _mockLogger.Verify(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Assigning job")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task FailedJob_ShouldStillTrackDuration()
        {
            // Arrange
            var jobName = "failed-test-job";
            var capturedPatches = new List<string>();

            // Setup mocks similar to above but with failed execution
            _mockVMExecutor.Setup(x => x.ExecuteRemoteAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<string>(),
                It.IsAny<List<EnvVar>>(),
                It.IsAny<TimeSpan>()))
                .ReturnsAsync(new ExecutionResult
                {
                    Success = false,
                    ExitCode = -1,
                    StandardOutput = "",
                    StandardError = "Command failed",
                    ExecutionTime = TimeSpan.FromMilliseconds(2000)
                });

            // Mock getting the current job with startTime
            var startTime = DateTime.UtcNow.AddSeconds(-2);
            _mockCustomObjects.Setup(x => x.GetNamespacedCustomObjectAsync(
                "orchestrator.vmjobs.io", "v1", "default",
                "vmjobs", It.IsAny<string>()))
                .ReturnsAsync(new { status = new { startTime = startTime.ToString("o") } });

            // Mock patching
            _mockCustomObjects.Setup(x => x.PatchNamespacedCustomObjectStatusAsync(
                It.IsAny<V1Patch>(),
                "orchestrator.vmjobs.io", "v1", "default",
                It.IsAny<string>(), "vmjobs"))
                .Callback<V1Patch, string, string, string, string, string>(
                    (patch, group, version, ns, name, plural) => capturedPatches.Add(patch.Content))
                .ReturnsAsync(new object());

            // Act - Call UpdateJobStatusAsync directly
            var updateMethod = typeof(KubernetesStyleController).GetMethod("UpdateJobStatusAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)updateMethod.Invoke(_controller, new object[] { jobName, "Failed", "Command failed", "test-vm", -1 });

            // Assert
            capturedPatches.Should().HaveCount(1);
            var failedPatch = capturedPatches[0];
            failedPatch.Should().Contain("\"phase\":\"Failed\"");
            failedPatch.Should().Contain("duration");
            failedPatch.Should().Contain("durationSeconds");
            
            dynamic patchObj = JsonConvert.DeserializeObject(failedPatch);
            ((string)patchObj.status.duration).Should().Be("2s");
            ((int)patchObj.status.exitCode).Should().Be(-1);
        }
    }
}
