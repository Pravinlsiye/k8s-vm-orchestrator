using System;
using System.Collections.Generic;
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
    public class DurationTrackingTests
    {
        private readonly Mock<IKubernetes> _mockKubernetesClient;
        private readonly Mock<ILogger<KubernetesStyleController>> _mockLogger;
        private readonly Mock<IVMExecutor> _mockVMExecutor;
        private readonly KubernetesStyleController _controller;

        public DurationTrackingTests()
        {
            _mockKubernetesClient = new Mock<IKubernetes>();
            _mockLogger = new Mock<ILogger<KubernetesStyleController>>();
            _mockVMExecutor = new Mock<IVMExecutor>();

            // Setup mock for CustomObjects property
            var mockCustomObjects = new Mock<ICustomObjectsOperations>();
            _mockKubernetesClient.Setup(x => x.CustomObjects).Returns(mockCustomObjects.Object);

            _controller = new KubernetesStyleController(_mockKubernetesClient.Object, _mockLogger.Object, _mockVMExecutor.Object);
        }

        [Theory]
        [InlineData(30, "30s")]
        [InlineData(90, "1m30s")]
        [InlineData(3600, "1h0m0s")]
        [InlineData(3665, "1h1m5s")]
        [InlineData(7200, "2h0m0s")]
        [InlineData(86400, "1d0h0m")]
        [InlineData(93665, "1d2h1m")]
        public void FormatDuration_ShouldFormatCorrectly(int totalSeconds, string expected)
        {
            // Arrange
            var duration = TimeSpan.FromSeconds(totalSeconds);

            // Act - We'll use reflection to test the private method
            var formatMethod = typeof(KubernetesStyleController).GetMethod("FormatDuration", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = formatMethod?.Invoke(_controller, new object[] { duration }) as string;

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public async Task UpdateJobStatus_Running_ShouldSetStartTime()
        {
            // Arrange
            var jobName = "test-job";
            var phase = "Running";
            var assignedVM = "vm-1";
            var capturedPatch = "";

            // Mock the current job (no existing startTime)
            var currentJob = new
            {
                status = new { }
            };

            _mockKubernetesClient.Setup(x => x.CustomObjects.GetNamespacedCustomObjectAsync(
                "orchestrator.vmjobs.io", "v1", "default", 
                "vmjobs", jobName))
                .ReturnsAsync(currentJob);

            _mockKubernetesClient.Setup(x => x.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
                It.IsAny<V1Patch>(),
                "orchestrator.vmjobs.io", "v1", "default", 
                jobName, "vmjobs"))
                .Callback<V1Patch, string, string, string, string, string>((patch, group, version, ns, name, plural) =>
                {
                    capturedPatch = patch.Content;
                })
                .ReturnsAsync(new object());

            // Act - Use reflection to call private method
            var updateMethod = typeof(KubernetesStyleController).GetMethod("UpdateJobStatusAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)updateMethod.Invoke(_controller, new object[] { jobName, phase, null, assignedVM, 0 });

            // Assert
            capturedPatch.Should().NotBeNullOrEmpty();
            dynamic patchObject = JsonConvert.DeserializeObject(capturedPatch);
            ((string)patchObject.status.phase).Should().Be("Running");
            ((string)patchObject.status.assignedVM).Should().Be("vm-1");
            ((string)patchObject.status.startTime).Should().NotBeNullOrEmpty();
            
            // Verify startTime is a valid ISO8601 timestamp
            DateTime.Parse((string)patchObject.status.startTime).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task UpdateJobStatus_Completed_ShouldCalculateDuration()
        {
            // Arrange
            var jobName = "test-job";
            var phase = "Completed";
            var assignedVM = "vm-1";
            var startTime = DateTime.UtcNow.AddMinutes(-5);
            var capturedPatch = "";

            // Mock the current job with existing startTime
            var currentJob = new
            {
                status = new
                {
                    startTime = startTime.ToString("o")
                }
            };

            _mockKubernetesClient.Setup(x => x.CustomObjects.GetNamespacedCustomObjectAsync(
                "orchestrator.vmjobs.io", "v1", "default",
                "vmjobs", jobName))
                .ReturnsAsync(currentJob);

            _mockKubernetesClient.Setup(x => x.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
                It.IsAny<V1Patch>(),
                "orchestrator.vmjobs.io", "v1", "default",
                jobName, "vmjobs"))
                .Callback<V1Patch, string, string, string, string, string>((patch, group, version, ns, name, plural) =>
                {
                    capturedPatch = patch.Content;
                })
                .ReturnsAsync(new object());

            // Act
            var updateMethod = typeof(KubernetesStyleController).GetMethod("UpdateJobStatusAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)updateMethod.Invoke(_controller, new object[] { jobName, phase, "Job completed successfully", assignedVM, 0 });

            // Assert
            capturedPatch.Should().NotBeNullOrEmpty();
            dynamic patchObject = JsonConvert.DeserializeObject(capturedPatch);
            ((string)patchObject.status.phase).Should().Be("Completed");
            ((string)patchObject.status.completionTime).Should().NotBeNullOrEmpty();
            ((string)patchObject.status.duration).Should().Be("5m0s");
            ((int)patchObject.status.durationSeconds).Should().BeGreaterThanOrEqualTo(300);
        }

        [Fact]
        public async Task UpdateJobStatus_Failed_ShouldAlsoCalculateDuration()
        {
            // Arrange
            var jobName = "test-job";
            var phase = "Failed";
            var assignedVM = "vm-1";
            var startTime = DateTime.UtcNow.AddSeconds(-45);
            var capturedPatch = "";

            // Mock the current job with existing startTime
            var currentJob = new
            {
                status = new
                {
                    startTime = startTime.ToString("o")
                }
            };

            _mockKubernetesClient.Setup(x => x.CustomObjects.GetNamespacedCustomObjectAsync(
                "orchestrator.vmjobs.io", "v1", "default",
                "vmjobs", jobName))
                .ReturnsAsync(currentJob);

            _mockKubernetesClient.Setup(x => x.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
                It.IsAny<V1Patch>(),
                "orchestrator.vmjobs.io", "v1", "default",
                jobName, "vmjobs"))
                .Callback<V1Patch, string, string, string, string, string>((patch, group, version, ns, name, plural) =>
                {
                    capturedPatch = patch.Content;
                })
                .ReturnsAsync(new object());

            // Act
            var updateMethod = typeof(KubernetesStyleController).GetMethod("UpdateJobStatusAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)updateMethod.Invoke(_controller, new object[] { jobName, phase, "Job failed", assignedVM, -1 });

            // Assert
            capturedPatch.Should().NotBeNullOrEmpty();
            dynamic patchObject = JsonConvert.DeserializeObject(capturedPatch);
            ((string)patchObject.status.phase).Should().Be("Failed");
            ((string)patchObject.status.completionTime).Should().NotBeNullOrEmpty();
            ((string)patchObject.status.duration).Should().Be("45s");
            ((int)patchObject.status.durationSeconds).Should().BeGreaterThanOrEqualTo(45);
            ((int)patchObject.status.exitCode).Should().Be(-1);
        }

        [Fact]
        public async Task UpdateJobStatus_WithoutStartTime_ShouldNotCalculateDuration()
        {
            // Arrange
            var jobName = "test-job";
            var phase = "Completed";
            var assignedVM = "vm-1";
            var capturedPatch = "";

            // Mock the current job without startTime (edge case)
            var currentJob = new
            {
                status = new { }
            };

            _mockKubernetesClient.Setup(x => x.CustomObjects.GetNamespacedCustomObjectAsync(
                "orchestrator.vmjobs.io", "v1", "default",
                "vmjobs", jobName))
                .ReturnsAsync(currentJob);

            _mockKubernetesClient.Setup(x => x.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
                It.IsAny<V1Patch>(),
                "orchestrator.vmjobs.io", "v1", "default",
                jobName, "vmjobs"))
                .Callback<V1Patch, string, string, string, string, string>((patch, group, version, ns, name, plural) =>
                {
                    capturedPatch = patch.Content;
                })
                .ReturnsAsync(new object());

            // Act
            var updateMethod = typeof(KubernetesStyleController).GetMethod("UpdateJobStatusAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)updateMethod.Invoke(_controller, new object[] { jobName, phase, null, assignedVM, 0 });

            // Assert
            capturedPatch.Should().NotBeNullOrEmpty();
            dynamic patchObject = JsonConvert.DeserializeObject(capturedPatch);
            ((string)patchObject.status.phase).Should().Be("Completed");
            ((string)patchObject.status.completionTime).Should().NotBeNullOrEmpty();
            
            // Duration should not be set without startTime
            var statusObj = (Dictionary<string, object>)patchObject.status.ToObject<Dictionary<string, object>>();
            statusObj.Should().NotContainKey("duration");
            statusObj.Should().NotContainKey("durationSeconds");
        }
    }
}
