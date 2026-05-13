using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VMJobOrchestrator;
using Xunit;

namespace VMJobOrchestrator.Tests
{
    public class DurationTrackingTests
    {
        // K8s client v12 uses extension methods for the friendly API (GetNamespacedCustomObjectAsync etc.).
        // Moq cannot intercept extension methods, so we mock the underlying *WithHttpMessagesAsync methods
        // that the extensions delegate to.

        private readonly Mock<IKubernetes> _mockKubernetesClient;
        private readonly Mock<ICustomObjectsOperations> _mockCustomObjects;
        private readonly Mock<ILogger<KubernetesStyleController>> _mockLogger;
        private readonly Mock<IVMExecutor> _mockVMExecutor;
        private readonly KubernetesStyleController _controller;

        public DurationTrackingTests()
        {
            _mockKubernetesClient = new Mock<IKubernetes>();
            _mockCustomObjects = new Mock<ICustomObjectsOperations>();
            _mockKubernetesClient.Setup(x => x.CustomObjects).Returns(_mockCustomObjects.Object);

            _mockLogger = new Mock<ILogger<KubernetesStyleController>>();
            _mockVMExecutor = new Mock<IVMExecutor>();

            _controller = new KubernetesStyleController(_mockKubernetesClient.Object, _mockLogger.Object, _mockVMExecutor.Object);
        }

        // Keep ISO timestamps as strings (don't let JSON.NET auto-convert to DateTime).
        private static readonly JsonSerializerSettings JsonNoDateParse = new()
        {
            DateParseHandling = DateParseHandling.None
        };

        private static dynamic ParsePatch(string content) =>
            JsonConvert.DeserializeObject<JObject>(content, JsonNoDateParse)!;

        // Use JObject for the mocked Body because the controller does dynamic
        // member access (currentJob.status.startTime) and anonymous types defined
        // in this test assembly are internal — DLR can't access them from the
        // controller assembly. JObject is public and DLR-friendly.
        private void SetupGet(string jobName, JObject body)
        {
            _mockCustomObjects.Setup(x => x.GetNamespacedCustomObjectWithHttpMessagesAsync(
                    "orchestrator.vmjobs.io", "v1", "default", "vmjobs", jobName,
                    It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpOperationResponse<object> { Body = body });
        }

        private void SetupPatch(string jobName, Action<string> onPatch)
        {
            _mockCustomObjects.Setup(x => x.PatchNamespacedCustomObjectStatusWithHttpMessagesAsync(
                    It.IsAny<object>(),
                    "orchestrator.vmjobs.io", "v1", "default", "vmjobs", jobName,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool?>(),
                    It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<object, string, string, string, string, string, string, string, bool?,
                    IReadOnlyDictionary<string, IReadOnlyList<string>>, CancellationToken>(
                    (body, _, _, _, _, _, _, _, _, _, _) =>
                    {
                        var patch = (V1Patch)body;
                        onPatch((string)patch.Content);
                    })
                .ReturnsAsync(new HttpOperationResponse<object> { Body = new object() });
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
            var duration = TimeSpan.FromSeconds(totalSeconds);

            var formatMethod = typeof(KubernetesStyleController).GetMethod("FormatDuration",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = formatMethod?.Invoke(_controller, new object[] { duration }) as string;

            result.Should().Be(expected);
        }

        [Fact]
        public async Task UpdateJobStatus_Running_ShouldSetStartTime()
        {
            var jobName = "test-job";
            string capturedPatch = "";

            SetupGet(jobName, new JObject { ["status"] = new JObject() });
            SetupPatch(jobName, p => capturedPatch = p);

            var updateMethod = typeof(KubernetesStyleController).GetMethod("UpdateJobStatusAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)updateMethod!.Invoke(_controller, new object?[] { jobName, "Running", null, "vm-1", 0 })!;

            capturedPatch.Should().NotBeNullOrEmpty();
            dynamic patchObject = ParsePatch(capturedPatch);
            ((string)patchObject.status.phase).Should().Be("Running");
            ((string)patchObject.status.assignedVM).Should().Be("vm-1");
            ((string)patchObject.status.startTime).Should().NotBeNullOrEmpty();

            DateTime.Parse((string)patchObject.status.startTime, null, System.Globalization.DateTimeStyles.RoundtripKind)
                .Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task UpdateJobStatus_Completed_ShouldCalculateDuration()
        {
            var jobName = "test-job";
            var startTime = DateTime.UtcNow.AddMinutes(-5);
            string capturedPatch = "";

            SetupGet(jobName, new JObject
            {
                ["status"] = new JObject { ["startTime"] = startTime.ToString("o") }
            });
            SetupPatch(jobName, p => capturedPatch = p);

            var updateMethod = typeof(KubernetesStyleController).GetMethod("UpdateJobStatusAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)updateMethod!.Invoke(_controller, new object?[] { jobName, "Completed", "Job completed successfully", "vm-1", 0 })!;

            capturedPatch.Should().NotBeNullOrEmpty();
            dynamic patchObject = ParsePatch(capturedPatch);
            ((string)patchObject.status.phase).Should().Be("Completed");
            ((string)patchObject.status.completionTime).Should().NotBeNullOrEmpty();
            ((string)patchObject.status.duration).Should().Be("5m0s");
            ((int)patchObject.status.durationSeconds).Should().BeGreaterThanOrEqualTo(300);
        }

        [Fact]
        public async Task UpdateJobStatus_Failed_ShouldAlsoCalculateDuration()
        {
            var jobName = "test-job";
            var startTime = DateTime.UtcNow.AddSeconds(-45);
            string capturedPatch = "";

            SetupGet(jobName, new JObject
            {
                ["status"] = new JObject { ["startTime"] = startTime.ToString("o") }
            });
            SetupPatch(jobName, p => capturedPatch = p);

            var updateMethod = typeof(KubernetesStyleController).GetMethod("UpdateJobStatusAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)updateMethod!.Invoke(_controller, new object?[] { jobName, "Failed", "Job failed", "vm-1", -1 })!;

            capturedPatch.Should().NotBeNullOrEmpty();
            dynamic patchObject = ParsePatch(capturedPatch);
            ((string)patchObject.status.phase).Should().Be("Failed");
            ((string)patchObject.status.completionTime).Should().NotBeNullOrEmpty();
            ((string)patchObject.status.duration).Should().Be("45s");
            ((int)patchObject.status.durationSeconds).Should().BeGreaterThanOrEqualTo(45);
            ((int)patchObject.status.exitCode).Should().Be(-1);
        }

        [Fact]
        public async Task UpdateJobStatus_WithoutStartTime_ShouldNotCalculateDuration()
        {
            var jobName = "test-job";
            string capturedPatch = "";

            SetupGet(jobName, new JObject { ["status"] = new JObject() });
            SetupPatch(jobName, p => capturedPatch = p);

            var updateMethod = typeof(KubernetesStyleController).GetMethod("UpdateJobStatusAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)updateMethod!.Invoke(_controller, new object?[] { jobName, "Completed", null, "vm-1", 0 })!;

            capturedPatch.Should().NotBeNullOrEmpty();
            dynamic patchObject = ParsePatch(capturedPatch);
            ((string)patchObject.status.phase).Should().Be("Completed");
            ((string)patchObject.status.completionTime).Should().NotBeNullOrEmpty();

            var statusObj = (JObject)patchObject.status;
            statusObj.Should().NotContainKey("duration");
            statusObj.Should().NotContainKey("durationSeconds");
        }
    }
}
