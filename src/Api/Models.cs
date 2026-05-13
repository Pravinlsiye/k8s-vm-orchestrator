using k8s.Models;
using System.Text.Json.Serialization;

namespace VMJobAPI.Models;

// Request model for creating a VMJob - supports both simple and advanced formats
public class CreateVMJobRequest
{
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = "default";
    public string? Description { get; set; }
    
    // Simple format support
    public string? Command { get; set; }
    public List<string>? Commands { get; set; }
    public ScriptBlock? Script { get; set; }
    public List<PipelineStep>? Pipeline { get; set; }
    
    // Advanced stage-based format
    public Dictionary<string, object>? Variables { get; set; }
    public List<EnvironmentVariable>? Environment { get; set; }
    public List<VMJobStage>? Stages { get; set; }
    public FinallyBlock? Finally { get; set; }
    
    // Advanced options
    public AdvancedOptions? Advanced { get; set; }
    
    // Legacy support (will be converted internally)
    public VMJobSpec? Spec { get; set; }
    
    // Job template support
    public string? Template { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
}

// VMJob models matching the CRD structure
public class VMJob
{
    public V1ObjectMeta Metadata { get; set; } = new();
    public VMJobSpec Spec { get; set; } = new();
    public VMJobStatus Status { get; set; } = new();
}

public class VMJobSpec
{
    public VMSelector VMSelector { get; set; } = new();
    public string Command { get; set; } = "powershell.exe";
    public List<string> Args { get; set; } = new();
    public string? WorkingDir { get; set; } = "C:\\";
    public string Timeout { get; set; } = "2h";
    public int Priority { get; set; } = 50;
    
    // New fields for stage-based execution
    public Dictionary<string, object>? Variables { get; set; }
    public List<EnvironmentVariable>? Environment { get; set; }
    public List<VMJobStage>? Stages { get; set; }
    public FinallyBlock? Finally { get; set; }
    public string? ExecutionMode { get; set; } // "simple" or "stages"
}

public class VMSelector
{
    public string OS { get; set; } = "windows";
    public Dictionary<string, string>? Tags { get; set; }
    public string? NodeName { get; set; }
}

public class VMJobStatus
{
    public string Phase { get; set; } = "Pending";
    public string? AssignedVM { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? CompletionTime { get; set; }
    public int? ExitCode { get; set; }
    public string? Logs { get; set; }
    public string? Message { get; set; }
    
    // Stage execution tracking
    public List<StageStatus>? Stages { get; set; }
    public string? CurrentStage { get; set; }
    public string? CurrentTask { get; set; }
    public int? TotalStages { get; set; }
    public int? CompletedStages { get; set; }
}

// Response models
public class VMJobResponse
{
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string? AssignedVM { get; set; }
    public string? AssignedVMIP { get; set; }
    public string? AssignedVMOS { get; set; }
    public int? ExitCode { get; set; }
    public string? Logs { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? CompletionTime { get; set; }
    
    // Stage information
    public List<StageStatus>? Stages { get; set; }
    public string? CurrentStage { get; set; }
    public string? CurrentTask { get; set; }
    public int? TotalStages { get; set; }
    public int? CompletedStages { get; set; }
}

// New model classes for simplified API
public class ScriptBlock
{
    public string Type { get; set; } = "powershell"; // powershell, batch, python
    public string Content { get; set; } = string.Empty;
}

public class PipelineStep
{
    public string Name { get; set; } = string.Empty;
    public string? Command { get; set; }
    public ScriptBlock? Script { get; set; }
    [JsonPropertyName("continue_on_error")]
    public bool ContinueOnError { get; set; } = false;
}

public class AdvancedOptions
{
    public string? WorkingDir { get; set; }
    public string? Timeout { get; set; }
    public int? Priority { get; set; }
    public string? TargetVM { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    public Dictionary<string, string>? Env { get; set; }
}

// For multi-command jobs, we'll track each step
public class JobStep
{
    public string StepName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

// Template definitions
public class JobTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Commands { get; set; } = new();
    public Dictionary<string, string> DefaultParameters { get; set; } = new();
}

// New models for stage-based execution
public class EnvironmentVariable
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class VMJobStage
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public List<string>? DependsOn { get; set; }
    public List<EnvironmentVariable>? Environment { get; set; }
    public List<VMJobTask> Steps { get; set; } = new();
}

public class VMJobTask
{
    public string Task { get; set; } = "PowerShell"; // PowerShell, Command, Python
    public string? DisplayName { get; set; }
    public string? WorkingDirectory { get; set; }
    public bool NewTerminal { get; set; } = false;
    public List<EnvironmentVariable>? Environment { get; set; }
    public TaskInputs Inputs { get; set; } = new();
    public bool ContinueOnError { get; set; } = false;
    public int RetryCountOnFailure { get; set; } = 0;
    public int? DelayBetweenRetries { get; set; }
}

public class TaskInputs
{
    public object? Script { get; set; } // Can be string or List<string>
    public string? Command { get; set; }
    public string? FilePath { get; set; }
    public string? Arguments { get; set; }
}

public class FinallyBlock
{
    public List<VMJobTask> Steps { get; set; } = new();
}

// Extended VMJobStatus to track stage execution
public class StageStatus
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Running, Completed, Failed
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public List<TaskStatus> Tasks { get; set; } = new();
}

public class TaskStatus
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int? ExitCode { get; set; }
    public string? Output { get; set; }
    public int RetryCount { get; set; } = 0;
}
