using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace VMJobOrchestrator
{
    /// <summary>
    /// Kubernetes-style controller that follows the reconciliation pattern
    /// with event-driven, non-blocking job scheduling
    /// </summary>
    public class KubernetesStyleController : BackgroundService
    {
        private readonly IKubernetes _kubernetesClient;
        private readonly ILogger<KubernetesStyleController> _logger;
        private readonly IVMExecutor _executor;
        
        // Constants
        private const string Group = "orchestrator.vmjobs.io";
        private const string Version = "v1";
        private const string VMJobPlural = "vmjobs";
        private const string VMNodePlural = "vmnodes";
        
        // State management - using concurrent collections for thread safety
        private readonly ConcurrentDictionary<string, VMNodeState> _vmStates = new();
        private readonly ConcurrentDictionary<string, JobExecutionTask> _runningJobs = new();
        
        // Configuration
        private readonly TimeSpan _reconcileInterval = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _monitorInterval = TimeSpan.FromSeconds(3);
        
        public KubernetesStyleController(
            IKubernetes kubernetesClient,
            ILogger<KubernetesStyleController> logger,
            IVMExecutor executor)
        {
            _kubernetesClient = kubernetesClient;
            _logger = logger;
            _executor = executor;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Kubernetes-style VMJob Controller");
            
            // Start multiple concurrent loops
            var tasks = new[]
            {
                ReconcileLoopAsync(stoppingToken),
                MonitorLoopAsync(stoppingToken),
                VMStateRefreshLoopAsync(stoppingToken)
            };
            
            await Task.WhenAll(tasks);
        }
        
        /// <summary>
        /// Main reconciliation loop - finds pending jobs and assigns them to available VMs
        /// </summary>
        private async Task ReconcileLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ReconcileJobsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in reconciliation loop");
                }
                
                await Task.Delay(_reconcileInterval, cancellationToken);
            }
        }
        
        /// <summary>
        /// Monitoring loop - checks status of running jobs and updates their state
        /// </summary>
        private async Task MonitorLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await MonitorRunningJobsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in monitoring loop");
                }
                
                await Task.Delay(_monitorInterval, cancellationToken);
            }
        }
        
        /// <summary>
        /// VM state refresh loop - keeps VM node information up to date
        /// </summary>
        private async Task VMStateRefreshLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RefreshVMStatesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing VM states");
                }
                
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
        
        /// <summary>
        /// Core reconciliation logic - assigns pending jobs to available VMs
        /// </summary>
        private async Task ReconcileJobsAsync()
        {
            // Get all jobs
            var jobs = await GetAllJobsAsync();
            var pendingJobs = jobs.Where(j => IsPendingJob(j)).ToList();
            
            if (!pendingJobs.Any())
                return;
                
            _logger.LogInformation($"Found {pendingJobs.Count} pending jobs");
            
            // Get available VMs
            var availableVMs = GetAvailableVMs();
            
            if (!availableVMs.Any())
            {
                _logger.LogInformation("No available VMs for job assignment");
                return;
            }
            
            // Assign jobs to VMs in parallel
            var assignments = new List<Task>();
            
            foreach (var job in pendingJobs)
            {
                // Get VMs that match the job's selector criteria
                var eligibleVMs = GetEligibleVMsForJob(job, availableVMs);
                
                if (eligibleVMs.Count == 0)
                {
                    var jobName = job.metadata.name.ToString();
                    var selector = job.spec?.vmSelector;
                    _logger.LogWarning($"No eligible VMs found for job {jobName}. Selector: nodeName={selector?.nodeName}, os={selector?.os}, tags={selector?.tags}");
                    continue;
                }
                
                // Select the VM with lowest load from eligible VMs
                string selectedVM = null;
                int lowestLoad = int.MaxValue;
                foreach (var vm in eligibleVMs)
                {
                    var load = GetVMLoad(vm);
                    if (load < lowestLoad)
                    {
                        lowestLoad = load;
                        selectedVM = vm;
                    }
                }
                
                if (selectedVM == null)
                    continue;
                
                // Remove selected VM from available list to prevent double assignment
                // unless the job specifically requested this VM by name
                var hasSpecificNode = job.spec?.vmSelector?.nodeName != null;
                if (!hasSpecificNode)
                {
                    availableVMs.Remove(selectedVM);
                }
                
                assignments.Add(AssignJobToVMAsync(job, selectedVM));
            }
            
            // Wait for all assignments to complete
            await Task.WhenAll(assignments);
            
            _logger.LogInformation($"Assigned {assignments.Count} jobs in this reconciliation cycle");
        }
        
        /// <summary>
        /// Assigns a job to a VM and starts execution asynchronously
        /// </summary>
        private async Task AssignJobToVMAsync(dynamic job, string vmName)
        {
            var jobName = job.metadata.name.ToString();
            
            try
            {
                _logger.LogInformation($"Assigning job {jobName} to VM {vmName}");
                
                // Update job status to Running with assigned VM
                await UpdateJobStatusAsync(jobName, "Running", null, vmName);
                
                // Mark VM as busy
                if (_vmStates.TryGetValue(vmName, out var vmState))
                {
                    vmState.IsBusy = true;
                    vmState.CurrentJobs.Add(jobName);
                    vmState.Status = "Busy";
                    vmState.LastHeartbeat = DateTime.UtcNow;
                    
                    // Update VMNode status immediately
                    await UpdateVMNodeStatusAsync(vmName, vmState);
                }
                
                // Start job execution asynchronously (non-blocking)
                var executionTask = Task.Run(async () => await ExecuteJobOnVMAsync(job, vmName));
                
                // Track the running job
                _runningJobs[jobName] = new JobExecutionTask
                {
                    JobName = jobName,
                    VMName = vmName,
                    Task = executionTask,
                    StartTime = DateTime.UtcNow
                };
                
                _logger.LogInformation($"Job {jobName} dispatched to VM {vmName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to assign job {jobName} to VM {vmName}");
                
                // Mark VM as available again
                if (_vmStates.TryGetValue(vmName, out var vmState))
                {
                    vmState.IsBusy = false;
                    vmState.CurrentJobs.Remove(jobName);
                    if (!vmState.CurrentJobs.Any())
                    {
                        vmState.Status = "Ready";
                    }
                    
                    // Update VMNode status
                    await UpdateVMNodeStatusAsync(vmName, vmState);
                }
            }
        }
        
        /// <summary>
        /// Executes a job on a VM (runs in background)
        /// </summary>
        private async Task ExecuteJobOnVMAsync(dynamic job, string vmName)
        {
            var jobName = job.metadata.name.ToString();
            
            try
            {
                if (!_vmStates.TryGetValue(vmName, out var vmState))
                {
                    throw new InvalidOperationException($"VM {vmName} not found in state");
                }
                
                var vmNode = vmState.Node;
                var vmIP = vmNode.spec.privateIP.ToString();
                var port = vmNode.spec.winrmConfig?.port?.ToObject<int>() ?? 5985;
                
                // Extract job execution details
                var command = job.spec.command.ToString();
                var args = job.spec.args?.ToObject<List<string>>() ?? new List<string>();
                var workingDir = job.spec.workingDir?.ToString() ?? @"C:\vmjob-results";
                var timeoutStr = job.spec.timeout?.ToString() ?? "2h";
                var timeout = ParseTimeout(timeoutStr);
                
                _logger.LogInformation($"Executing job {jobName} on {vmIP}:{port}");
                
                // Handle large scripts
                ExecutionResult result;
                if (ShouldUseFileBasedExecution(command, args))
                {
                    result = await ExecuteLargeScriptAsync(jobName, vmIP, port, command, args, workingDir, timeout);
                }
                else
                {
                    result = await _executor.ExecuteRemoteAsync(
                        host: vmIP,
                        port: port,
                        command: command,
                        args: args,
                        workingDir: workingDir,
                        envVars: new List<EnvVar>(),
                        timeout: timeout);
                }
                
                // Update job with results
                if (result.Success)
                {
                    await UpdateJobStatusAsync(jobName, "Completed", result.StandardOutput, vmName, result.ExitCode);
                }
                else
                {
                    await UpdateJobStatusAsync(jobName, "Failed", result.StandardError ?? result.StandardOutput, vmName, result.ExitCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing job {jobName} on VM {vmName}");
                await UpdateJobStatusAsync(jobName, "Failed", ex.Message, vmName, -1);
            }
            finally
            {
                // Mark VM as available and remove job from tracking
                if (_vmStates.TryGetValue(vmName, out var vmState))
                {
                    vmState.CurrentJobs.Remove(jobName);
                    vmState.JobsCompleted++;
                    vmState.LastHeartbeat = DateTime.UtcNow;
                    
                    if (!vmState.CurrentJobs.Any())
                    {
                        vmState.IsBusy = false;
                        vmState.Status = "Ready";
                    }
                    
                    // Update VMNode status
                    await UpdateVMNodeStatusAsync(vmName, vmState);
                }
                
                _runningJobs.TryRemove(jobName, out JobExecutionTask removedJob);
                
                _logger.LogInformation($"Job {jobName} execution completed on VM {vmName}");
            }
        }
        
        /// <summary>
        /// Monitors running jobs and cleans up completed ones
        /// </summary>
        private async Task MonitorRunningJobsAsync()
        {
            var completedJobs = new List<string>();
            
            foreach (var kvp in _runningJobs)
            {
                var jobTask = kvp.Value;
                
                if (jobTask.Task.IsCompleted)
                {
                    completedJobs.Add(kvp.Key);
                    
                    // Log any exceptions
                    if (jobTask.Task.IsFaulted)
                    {
                        _logger.LogError(jobTask.Task.Exception, $"Job {kvp.Key} task faulted");
                    }
                }
                else if (DateTime.UtcNow - jobTask.StartTime > TimeSpan.FromMinutes(120))
                {
                    // Timeout check - jobs shouldn't run more than 2 hours
                    _logger.LogWarning($"Job {kvp.Key} has been running for over 2 hours");
                }
            }
            
            // Clean up completed jobs
            foreach (var jobName in completedJobs)
            {
                _runningJobs.TryRemove(jobName, out JobExecutionTask removedJob);
            }
            
            if (completedJobs.Any())
            {
                _logger.LogInformation($"Cleaned up {completedJobs.Count} completed jobs");
            }
        }
        
        /// <summary>
        /// Refreshes VM node states from Kubernetes
        /// </summary>
        private async Task RefreshVMStatesAsync()
        {
            try
            {
                var response = await _kubernetesClient.CustomObjects.ListClusterCustomObjectWithHttpMessagesAsync(
                    group: Group,
                    version: Version,
                    plural: VMNodePlural);
                
                var nodeList = JsonConvert.DeserializeObject<dynamic>(response.Body.ToString());
                
                if (nodeList?.items != null)
                {
                    foreach (var node in nodeList.items)
                    {
                        var name = node.metadata.name.ToString();
                        
                        if (!_vmStates.ContainsKey(name))
                        {
                            _vmStates[name] = new VMNodeState
                            {
                                Name = name,
                                Node = node,
                                IsBusy = false,
                                CurrentJobs = new HashSet<string>(),
                                Status = "Ready",
                                LastHeartbeat = DateTime.UtcNow,
                                JobsCompleted = 0
                            };
                            
                            _logger.LogInformation($"Discovered new VM node: {name}");
                            
                            // Update VMNode status for newly discovered VM
                            await UpdateVMNodeStatusAsync(name, _vmStates[name]);
                        }
                        else
                        {
                            _vmStates[name].Node = node; // Update node info
                            
                            // Validate and clean up CurrentJobs against actual running jobs
                            var vmState = _vmStates[name];
                            var jobsToRemove = new List<string>();
                            
                            foreach (var jobName in vmState.CurrentJobs)
                            {
                                if (!_runningJobs.ContainsKey(jobName))
                                {
                                    jobsToRemove.Add(jobName);
                                    _logger.LogWarning($"Removing orphaned job {jobName} from VM {name}");
                                }
                            }
                            
                            foreach (var jobName in jobsToRemove)
                            {
                                vmState.CurrentJobs.Remove(jobName);
                            }
                            
                            // Ensure VM is marked as available if no jobs are running
                            if (vmState.CurrentJobs.Count == 0)
                            {
                                vmState.IsBusy = false;
                                vmState.Status = "Ready";
                            }
                            else
                            {
                                vmState.Status = "Busy";
                            }
                            
                            // Update VMNode status in Kubernetes
                            await UpdateVMNodeStatusAsync(name, vmState);
                        }
                    }
                }
                
                _logger.LogInformation($"Refreshed {_vmStates.Count} VM nodes");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh VM states");
            }
        }
        
        /// <summary>
        /// Gets all VMJobs from Kubernetes
        /// </summary>
        private async Task<List<dynamic>> GetAllJobsAsync()
        {
            var response = await _kubernetesClient.CustomObjects.ListNamespacedCustomObjectWithHttpMessagesAsync(
                group: Group,
                version: Version,
                namespaceParameter: "default",
                plural: VMJobPlural);
                
            var jobList = JsonConvert.DeserializeObject<dynamic>(response.Body.ToString());
            var jobs = new List<dynamic>();
            
            if (jobList?.items != null)
            {
                foreach (var job in jobList.items)
                {
                    jobs.Add(job);
                }
            }
            
            return jobs;
        }
        
        /// <summary>
        /// Gets available VMs for job assignment
        /// </summary>
        private List<string> GetAvailableVMs()
        {
            return _vmStates
                .Where(kvp => !kvp.Value.IsBusy && kvp.Value.Node != null)
                .Select(kvp => kvp.Key)
                .ToList();
        }
        
        /// <summary>
        /// Gets VMs that are eligible for a specific job based on its selector criteria
        /// </summary>
        private List<string> GetEligibleVMsForJob(dynamic job, List<string> availableVMs)
        {
            var vmSelector = job.spec?.vmSelector;
            if (vmSelector == null)
            {
                // No selector specified, all available VMs are eligible
                return new List<string>(availableVMs);
            }
            
            var eligibleVMs = new List<string>();
            var jobName = job.metadata.name.ToString();
            
            _logger.LogDebug($"Evaluating VM eligibility for job {jobName}");
            
            foreach (var vmName in availableVMs)
            {
                if (!_vmStates.TryGetValue(vmName, out var vmState) || vmState.Node == null)
                    continue;
                    
                var vmNode = vmState.Node;
                var vmSpec = vmNode.spec;
                
                // Check if VM matches the selector criteria
                bool matches = true;
                
                // Check specific node name
                if (vmSelector.nodeName != null && vmSelector.nodeName.ToString() != vmName)
                {
                    matches = false;
                }
                
                // Check OS requirement
                if (matches && vmSelector.os != null && vmSpec.os != null && 
                    vmSelector.os.ToString().ToLower() != vmSpec.os.ToString().ToLower())
                {
                    matches = false;
                }
                
                // Check tags
                if (matches && vmSelector.tags != null)
                {
                    var requiredTags = vmSelector.tags as Newtonsoft.Json.Linq.JObject;
                    var vmTags = vmSpec.tags as Newtonsoft.Json.Linq.JObject;
                    
                    if (vmTags == null)
                    {
                        matches = false;
                    }
                    else if (requiredTags != null)
                    {
                        // Check if all required tags match
                        foreach (var tag in requiredTags.Properties())
                        {
                            var tagName = tag.Name;
                            var tagValue = tag.Value?.ToString();
                            
                            var vmTagValue = vmTags[tagName];
                            if (vmTagValue == null || vmTagValue.ToString() != tagValue)
                            {
                                matches = false;
                                break;
                            }
                        }
                    }
                }
                
                if (matches)
                {
                    eligibleVMs.Add(vmName);
                    _logger.LogDebug($"VM {vmName} is eligible for job {jobName}");
                }
                else
                {
                    _logger.LogDebug($"VM {vmName} is not eligible for job {jobName}");
                }
            }
            
            _logger.LogInformation($"Found {eligibleVMs.Count} eligible VMs for job {jobName}");
            return eligibleVMs;
        }
        
        /// <summary>
        /// Gets the current load on a VM (number of jobs)
        /// </summary>
        private int GetVMLoad(string vmName)
        {
            return _vmStates.TryGetValue(vmName, out var state) ? state.CurrentJobs.Count : 0;
        }
        
        /// <summary>
        /// Checks if a job is pending
        /// </summary>
        private bool IsPendingJob(dynamic job)
        {
            var phase = job.status?.phase?.ToString();
            return string.IsNullOrEmpty(phase) || phase == "Pending";
        }
        
        /// <summary>
        /// Updates VMNode status in Kubernetes
        /// </summary>
        private async Task UpdateVMNodeStatusAsync(string vmName, VMNodeState vmState)
        {
            try
            {
                _logger.LogInformation($"Updating VMNode {vmName} status to {vmState.Status}");
                
                var status = new
                {
                    status = new
                    {
                        phase = vmState.Status,
                        currentJobs = vmState.CurrentJobs.ToArray(),
                        jobsRunning = vmState.CurrentJobs.Count,
                        jobsCompleted = vmState.JobsCompleted,
                        lastHeartbeat = DateTime.UtcNow.ToString("o")
                    }
                };
                
                var patchDocument = JsonConvert.SerializeObject(status);
                _logger.LogDebug($"Patch document: {patchDocument}");
                
                await _kubernetesClient.CustomObjects.PatchClusterCustomObjectStatusAsync(
                    body: new V1Patch(patchDocument, V1Patch.PatchType.MergePatch),
                    group: Group,
                    version: Version,
                    plural: VMNodePlural,
                    name: vmName);
                    
                _logger.LogInformation($"Successfully updated VMNode {vmName} status to {vmState.Status}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to update VMNode {vmName} status. Error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Updates job status in Kubernetes
        /// </summary>
        private async Task UpdateJobStatusAsync(string jobName, string phase, string logs, string assignedVM, int exitCode = 0)
        {
            try
            {
                // Get current job to preserve existing timestamps
                var currentJob = await _kubernetesClient.CustomObjects.GetNamespacedCustomObjectAsync(
                    group: Group,
                    version: Version,
                    namespaceParameter: "default",
                    plural: VMJobPlural,
                    name: jobName) as dynamic;

                // Build status object based on phase
                var statusObj = new Dictionary<string, object>
                {
                    ["phase"] = phase,
                    ["assignedVM"] = assignedVM,
                    ["exitCode"] = exitCode,
                    ["lastUpdated"] = DateTime.UtcNow.ToString("o")
                };

                // Add logs only if provided
                if (!string.IsNullOrEmpty(logs))
                {
                    statusObj["logs"] = logs;
                }

                // Handle timestamps and duration
                var now = DateTime.UtcNow;
                string startTimeStr = null;
                DateTime? startTime = null;
                
                // Safely extract startTime from JsonElement
                try
                {
                    if (currentJob is System.Text.Json.JsonElement jsonElement)
                    {
                        if (jsonElement.TryGetProperty("status", out var status) && 
                            status.TryGetProperty("startTime", out var startTimeElement))
                        {
                            startTimeStr = startTimeElement.GetString();
                        }
                    }
                    else if (currentJob?.status?.startTime != null)
                    {
                        startTimeStr = currentJob.status.startTime.ToString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract start time from current job");
                }

                if (phase == "Running")
                {
                    statusObj["startTime"] = now.ToString("o");
                }
                else if (!string.IsNullOrEmpty(startTimeStr))
                {
                    // Preserve existing start time
                    statusObj["startTime"] = startTimeStr;
                    // RoundtripKind keeps UTC strings as Kind=Utc; default Parse converts to Local
                    // which produces wrong durations when the host timezone is not UTC.
                    startTime = DateTime.Parse(startTimeStr, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind);
                }

                if (phase == "Completed" || phase == "Failed")
                {
                    statusObj["completionTime"] = now.ToString("o");
                    
                    // Calculate duration if we have a start time
                    if (startTime.HasValue)
                    {
                        var duration = now - startTime.Value;
                        statusObj["duration"] = FormatDuration(duration);
                        statusObj["durationSeconds"] = (int)duration.TotalSeconds;
                    }
                }

                var patchBody = new { status = statusObj };
                
                var patch = new k8s.Models.V1Patch(
                    JsonConvert.SerializeObject(patchBody),
                    k8s.Models.V1Patch.PatchType.MergePatch
                );
                
                await _kubernetesClient.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
                    body: patch,
                    group: Group,
                    version: Version,
                    namespaceParameter: "default",
                    name: jobName,
                    plural: VMJobPlural);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to update job status for {jobName}");
            }
        }

        /// <summary>
        /// Formats a duration for display
        /// </summary>
        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{(int)duration.TotalDays}d{duration.Hours}h{duration.Minutes}m";
            else if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h{duration.Minutes}m{duration.Seconds}s";
            else if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m{duration.Seconds}s";
            else
                return $"{(int)duration.TotalSeconds}s";
        }
        
        /// <summary>
        /// Determines if file-based execution should be used for large scripts
        /// </summary>
        private bool ShouldUseFileBasedExecution(string command, List<string> args)
        {
            return command.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase) && 
                   args.Count > 2 && 
                   args[args.Count - 1].Length > 7000;
        }
        
        /// <summary>
        /// Executes large scripts by writing to a file first
        /// </summary>
        private async Task<ExecutionResult> ExecuteLargeScriptAsync(
            string jobName, string vmIP, int port, string command, 
            List<string> args, string workingDir, TimeSpan timeout)
        {
            var scriptContent = args[args.Count - 1];
            var scriptFileName = $"vmjob_{jobName.Replace("-", "_")}.ps1";
            var scriptPath = $"C:\\vmjob-results\\{scriptFileName}";
            
            var writeAndExecuteScript = $@"
# Create directory if needed
if (!(Test-Path 'C:\vmjob-results')) {{ New-Item -ItemType Directory -Path 'C:\vmjob-results' -Force }}

# Write script content to file
@'
{scriptContent}
'@ | Out-File -FilePath '{scriptPath}' -Encoding UTF8 -Force

# Execute the script
& '{scriptPath}'
$exitCode = $LASTEXITCODE
if ($exitCode -eq $null) {{ $exitCode = 0 }}

# Clean up the script file
Remove-Item -Path '{scriptPath}' -Force -ErrorAction SilentlyContinue

# Exit with the same code
exit $exitCode
";
            
            var modifiedArgs = new List<string>(args);
            modifiedArgs[modifiedArgs.Count - 1] = writeAndExecuteScript;
            
            return await _executor.ExecuteRemoteAsync(
                host: vmIP,
                port: port,
                command: command,
                args: modifiedArgs,
                workingDir: workingDir,
                envVars: new List<EnvVar>(),
                timeout: timeout);
        }
        
        /// <summary>
        /// Parses timeout string to TimeSpan
        /// </summary>
        private TimeSpan ParseTimeout(string timeout)
        {
            if (string.IsNullOrEmpty(timeout))
                return TimeSpan.FromHours(2);
                
            if (timeout.EndsWith("m"))
            {
                if (int.TryParse(timeout.TrimEnd('m'), out var minutes))
                    return TimeSpan.FromMinutes(minutes);
            }
            else if (timeout.EndsWith("s"))
            {
                if (int.TryParse(timeout.TrimEnd('s'), out var seconds))
                    return TimeSpan.FromSeconds(seconds);
            }
            else if (timeout.EndsWith("h"))
            {
                if (int.TryParse(timeout.TrimEnd('h'), out var hours))
                    return TimeSpan.FromHours(hours);
            }
            
            return TimeSpan.FromHours(2);
        }
        
        /// <summary>
        /// Represents VM node state
        /// </summary>
        private class VMNodeState
        {
            public string Name { get; set; }
            public dynamic Node { get; set; }
            public bool IsBusy { get; set; }
            public HashSet<string> CurrentJobs { get; set; } = new();
            public string Status { get; set; } = "Ready";
            public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
            public int JobsCompleted { get; set; } = 0;
        }
        
        /// <summary>
        /// Represents a running job execution task
        /// </summary>
        private class JobExecutionTask
        {
            public string JobName { get; set; }
            public string VMName { get; set; }
            public Task Task { get; set; }
            public DateTime StartTime { get; set; }
        }
    }
}
