using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace VMJobOrchestrator
{
    public class ResultAggregator
    {
        private readonly ILogger<ResultAggregator> _logger;
        private readonly BlobContainerClient _containerClient;
        private readonly bool _useAzureStorage;

        public ResultAggregator(ILogger<ResultAggregator> logger, AzureStorageConfig storageConfig = null)
        {
            _logger = logger;

            if (storageConfig != null && !string.IsNullOrEmpty(storageConfig.AccountName))
            {
                try
                {
                    var connectionString = $"DefaultEndpointsProtocol=https;AccountName={storageConfig.AccountName};AccountKey={storageConfig.AccountKey};EndpointSuffix=core.windows.net";
                    var blobServiceClient = new BlobServiceClient(connectionString);
                    _containerClient = blobServiceClient.GetBlobContainerClient(storageConfig.ContainerName ?? "job-results");

                    // Ensure container exists
                    _containerClient.CreateIfNotExists();
                    _useAzureStorage = true;
                    _logger.LogInformation($"Azure Storage initialized: {storageConfig.AccountName}/{storageConfig.ContainerName}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Azure Storage");
                    _useAzureStorage = false;
                }
            }
        }

        public async Task<bool> CollectJobResultAsync(string jobName, string resultPath)
        {
            _logger.LogInformation($"Collecting result for job {jobName} from {resultPath}");

            if (!_useAzureStorage)
            {
                _logger.LogWarning("Azure Storage not configured, skipping result collection");
                return false;
            }

            try
            {
                // Create metadata
                var metadata = new JobResultMetadata
                {
                    JobName = jobName,
                    ResultPath = resultPath,
                    CollectionTime = DateTime.UtcNow,
                    Status = "collected"
                };

                // Upload metadata to blob storage
                var blobName = $"{jobName}/metadata.json";
                var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                await UploadToBlobAsync(blobName, metadataJson);
                _logger.LogInformation($"Result metadata uploaded for job {jobName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error collecting result for job {jobName}");
                return false;
            }
        }

        public async Task<AggregatedResults> AggregateResultsAsync(List<string> jobNames)
        {
            _logger.LogInformation($"Aggregating results for {jobNames.Count} jobs");

            var aggregated = new AggregatedResults
            {
                TotalJobs = jobNames.Count,
                Timestamp = DateTime.UtcNow,
                Jobs = new Dictionary<string, JobResult>()
            };

            if (!_useAzureStorage)
            {
                _logger.LogWarning("Azure Storage not configured, returning empty aggregation");
                return aggregated;
            }

            foreach (var jobName in jobNames)
            {
                try
                {
                    var blobName = $"{jobName}/metadata.json";
                    var blobClient = _containerClient.GetBlobClient(blobName);

                    if (await blobClient.ExistsAsync())
                    {
                        var response = await blobClient.DownloadContentAsync();
                        var content = response.Value.Content.ToString();
                        
                        aggregated.Jobs[jobName] = new JobResult
                        {
                            Status = "found",
                            Metadata = content
                        };
                    }
                    else
                    {
                        aggregated.Jobs[jobName] = new JobResult
                        {
                            Status = "not_found"
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error getting result for job {jobName}");
                    aggregated.Jobs[jobName] = new JobResult
                    {
                        Status = "error",
                        Error = ex.Message
                    };
                }
            }

            _logger.LogInformation($"Aggregated {aggregated.Jobs.Count} job results");
            return aggregated;
        }

        public async Task<bool> DownloadResultAsync(string jobName, string localPath)
        {
            if (!_useAzureStorage)
            {
                _logger.LogWarning("Azure Storage not configured");
                return false;
            }

            try
            {
                Directory.CreateDirectory(localPath);
                var downloadedCount = 0;

                await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: $"{jobName}/"))
                {
                    var blobClient = _containerClient.GetBlobClient(blobItem.Name);
                    var localFile = Path.Combine(localPath, Path.GetFileName(blobItem.Name));

                    await blobClient.DownloadToAsync(localFile);
                    downloadedCount++;
                    _logger.LogDebug($"Downloaded: {blobItem.Name} -> {localFile}");
                }

                _logger.LogInformation($"Downloaded {downloadedCount} files for job {jobName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading result for job {jobName}");
                return false;
            }
        }

        public async Task<List<string>> ListJobResultsAsync()
        {
            if (!_useAzureStorage)
            {
                return new List<string>();
            }

            try
            {
                var jobNames = new HashSet<string>();

                await foreach (var blobItem in _containerClient.GetBlobsAsync())
                {
                    var parts = blobItem.Name.Split('/');
                    if (parts.Length > 0)
                    {
                        jobNames.Add(parts[0]);
                    }
                }

                return new List<string>(jobNames);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing job results");
                return new List<string>();
            }
        }

        private async Task UploadToBlobAsync(string blobName, string content)
        {
            if (!_useAzureStorage)
                return;

            try
            {
                var blobClient = _containerClient.GetBlobClient(blobName);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                await blobClient.UploadAsync(stream, overwrite: true);
                _logger.LogDebug($"Uploaded blob: {blobName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading blob {blobName}");
                throw;
            }
        }
    }

    public class AzureStorageConfig
    {
        public string AccountName { get; set; }
        public string AccountKey { get; set; }
        public string ContainerName { get; set; }
    }

    public class JobResultMetadata
    {
        public string JobName { get; set; }
        public string ResultPath { get; set; }
        public DateTime CollectionTime { get; set; }
        public string Status { get; set; }
    }

    public class AggregatedResults
    {
        public int TotalJobs { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, JobResult> Jobs { get; set; }
        public string Error { get; set; }
    }

    public class JobResult
    {
        public string Status { get; set; }
        public string Metadata { get; set; }
        public string Error { get; set; }
    }
}
