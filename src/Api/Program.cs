using k8s;
using k8s.Models;
using VMJobAPI.Models;
using VMJobAPI.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "k8s-vm-orchestrator API",
        Version = "v1",
        Description = "REST API for submitting VMJobs (single commands, multi-commands, scripts, stage-based workflows) to a pool of Windows VMs managed as Kubernetes custom resources."
    });
});

// Configure Kubernetes client
builder.Services.AddSingleton<IKubernetes>(serviceProvider =>
{
    var config = KubernetesClientConfiguration.IsInCluster() 
        ? KubernetesClientConfiguration.InClusterConfig() 
        : KubernetesClientConfiguration.BuildConfigFromConfigFile();
    
    return new Kubernetes(config);
});

// Add job request processor
builder.Services.AddScoped<JobRequestProcessor>();

var app = builder.Build();

// Configure the HTTP request pipeline
// Enable Swagger for all environments
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "VMJob API V1");
    options.RoutePrefix = "swagger"; // Swagger will be at /swagger
});

// API Endpoints
app.MapPost("/api/vmjobs", async (CreateVMJobRequest request, IKubernetes client, JobRequestProcessor processor) =>
{
    try
    {
        // Generate name if not provided
        if (string.IsNullOrEmpty(request.Name))
        {
            request.Name = $"job-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0, 8)}";
        }

        // Process request to get VMJobSpec
        var spec = processor.ProcessRequest(request);

        // Create the VMJob object matching CRD structure
        var vmJob = new
        {
            apiVersion = "orchestrator.vmjobs.io/v1",
            kind = "VMJob",
            metadata = new
            {
                name = request.Name,
                @namespace = request.Namespace,
                labels = new Dictionary<string, string>
                {
                    ["created-by"] = "api",
                    ["job-type"] = DetermineJobType(request)
                }
            },
            spec = new
            {
                vmSelector = new
                {
                    os = spec.VMSelector.OS,
                    tags = spec.VMSelector.Tags,
                    nodeName = spec.VMSelector.NodeName
                },
                command = spec.Command,
                args = spec.Args,
                workingDir = spec.WorkingDir,
                timeout = spec.Timeout,
                priority = spec.Priority,
                executionMode = spec.ExecutionMode,
                variables = spec.Variables,
                environment = spec.Environment,
                stages = spec.Stages,
                @finally = spec.Finally
            }
        };

        // Create the VMJob using the custom resource API
        var result = await client.CustomObjects.CreateNamespacedCustomObjectAsync(
            body: vmJob,
            group: "orchestrator.vmjobs.io",
            version: "v1",
            namespaceParameter: request.Namespace,
            plural: "vmjobs"
        );

        // Return the actual created resource
        var jsonString = result?.ToString() ?? "{}";
        var doc = JsonDocument.Parse(jsonString);
        var root = doc.RootElement;

        var createdJob = new
        {
            name = request.Name,
            @namespace = request.Namespace,
            uid = root.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("uid", out var uid) 
                ? uid.GetString() 
                : null,
            resourceVersion = metadata.TryGetProperty("resourceVersion", out var rv) 
                ? rv.GetString() 
                : null,
            creationTimestamp = metadata.TryGetProperty("creationTimestamp", out var ct) 
                ? ct.GetString() 
                : null,
            spec = request.Spec,
            status = root.TryGetProperty("status", out var status) 
                ? new 
                {
                    phase = status.TryGetProperty("phase", out var phase) ? phase.GetString() : "Pending"
                }
                : new { phase = "Pending" }
        };

        return Results.Created($"/api/vmjobs/{request.Namespace}/{request.Name}", createdJob);
    }
    catch (k8s.Autorest.HttpOperationException ex)
    {
        return Results.Problem(
            detail: ex.Response.Content,
            statusCode: (int)ex.Response.StatusCode,
            title: "Failed to create VMJob"
        );
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Internal Server Error"
        );
    }
})
.WithName("CreateVMJob")
.WithOpenApi(operation => new(operation)
{
    Summary = "Create a new VMJob",
    Description = "Creates a new VMJob custom resource in the specified namespace",
    Tags = new List<Microsoft.OpenApi.Models.OpenApiTag> { new() { Name = "VMJobs" } }
});

app.MapGet("/api/vmjobs/{namespace}/{name}", async (string @namespace, string name, IKubernetes client) =>
{
    try
    {
        // Get the VMJob status
        var vmJob = await client.CustomObjects.GetNamespacedCustomObjectAsync(
            group: "orchestrator.vmjobs.io",
            version: "v1",
            namespaceParameter: @namespace,
            plural: "vmjobs",
            name: name
        );

        // Deserialize to JsonElement for easy access
        var jsonString = vmJob?.ToString() ?? "{}";
        var doc = JsonDocument.Parse(jsonString);
        var root = doc.RootElement;

        // Extract status information
        var response = new VMJobResponse
        {
            Name = name,
            Namespace = @namespace,
            Phase = root.TryGetProperty("status", out var status) && status.TryGetProperty("phase", out var phase) 
                ? phase.GetString() ?? "Unknown" 
                : "Pending"
        };

        // Extract other status fields if they exist
        if (root.TryGetProperty("status", out var statusObj))
        {
            if (statusObj.TryGetProperty("assignedVM", out var assignedVM))
            {
                response.AssignedVM = assignedVM.GetString();
                
                // Try to get VM node information
                if (!string.IsNullOrEmpty(response.AssignedVM))
                {
                    try
                    {
                        var vmNode = await client.CustomObjects.GetClusterCustomObjectAsync(
                            group: "orchestrator.vmjobs.io",
                            version: "v1",
                            plural: "vmnodes",
                            name: response.AssignedVM
                        );
                        
                        var vmNodeDoc = JsonDocument.Parse(vmNode?.ToString() ?? "{}");
                        var vmNodeRoot = vmNodeDoc.RootElement;
                        
                        if (vmNodeRoot.TryGetProperty("spec", out var vmNodeSpec))
                        {
                            if (vmNodeSpec.TryGetProperty("ipAddress", out var ipAddress))
                                response.AssignedVMIP = ipAddress.GetString();
                            if (vmNodeSpec.TryGetProperty("os", out var os))
                                response.AssignedVMOS = os.GetString();
                        }
                    }
                    catch
                    {
                        // VM node info not available, continue without it
                    }
                }
            }
            
            if (statusObj.TryGetProperty("exitCode", out var exitCode))
                response.ExitCode = exitCode.GetInt32();
            
            if (statusObj.TryGetProperty("logs", out var logs))
                response.Logs = logs.GetString();
            
            if (statusObj.TryGetProperty("startTime", out var startTime))
                response.StartTime = DateTime.Parse(startTime.GetString()!);
            
            if (statusObj.TryGetProperty("completionTime", out var completionTime))
                response.CompletionTime = DateTime.Parse(completionTime.GetString()!);
            
            // Stage information
            if (statusObj.TryGetProperty("stages", out var stages))
            {
                response.Stages = new List<StageStatus>();
                // Parse stages array if needed
            }
            
            if (statusObj.TryGetProperty("currentStage", out var currentStage))
                response.CurrentStage = currentStage.GetString();
            
            if (statusObj.TryGetProperty("currentTask", out var currentTask))
                response.CurrentTask = currentTask.GetString();
            
            if (statusObj.TryGetProperty("totalStages", out var totalStages))
                response.TotalStages = totalStages.GetInt32();
            
            if (statusObj.TryGetProperty("completedStages", out var completedStages))
                response.CompletedStages = completedStages.GetInt32();
        }

        return Results.Ok(response);
    }
    catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { message = $"VMJob '{name}' not found in namespace '{@namespace}'" });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Internal Server Error"
        );
    }
})
.WithName("GetVMJobStatus")
.WithOpenApi(operation => new(operation)
{
    Summary = "Get VMJob status",
    Description = "Retrieves the current status of a VMJob by namespace and name",
    Tags = new List<Microsoft.OpenApi.Models.OpenApiTag> { new() { Name = "VMJobs" } }
});

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("HealthCheck")
    .WithOpenApi(operation => new(operation)
    {
        Summary = "Health check",
        Description = "Simple health check endpoint to verify API is running",
        Tags = new List<Microsoft.OpenApi.Models.OpenApiTag> { new() { Name = "Health" } }
    });

app.Run();

// Helper method to determine job type based on request
string DetermineJobType(CreateVMJobRequest request)
{
    if (!string.IsNullOrEmpty(request.Template)) return "template";
    if (request.Pipeline != null && request.Pipeline.Any()) return "pipeline";
    if (request.Script != null) return "script";
    if (request.Commands != null && request.Commands.Any()) return "multi-command";
    if (!string.IsNullOrEmpty(request.Command)) return "single-command";
    if (request.Spec != null) return "legacy";
    return "unknown";
}
