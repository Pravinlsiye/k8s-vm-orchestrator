using k8s;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace VMJobOrchestrator
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;

                    // Configure logging
                    services.AddLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.SetMinimumLevel(LogLevel.Information);
                    });

                    // MongoDB removed - no longer needed

                    // Register Kubernetes client
                    services.AddSingleton<IKubernetes>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<Program>>();
                        
                        try
                        {
                            // Try in-cluster config first
                            var config = KubernetesClientConfiguration.InClusterConfig();
                            logger.LogInformation("Loaded in-cluster Kubernetes configuration");
                            return new Kubernetes(config);
                        }
                        catch
                        {
                            try
                            {
                                // Fall back to local kubeconfig
                                var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
                                logger.LogInformation("Loaded local Kubernetes configuration");
                                return new Kubernetes(config);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Failed to load Kubernetes configuration");
                                throw;
                            }
                        }
                    });

                    // Register VM credentials
                    services.AddSingleton<VMCredentials>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<Program>>();
                        var username = configuration["WinRM:Username"] 
                            ?? Environment.GetEnvironmentVariable("VM_ADMIN_USERNAME") 
                            ?? "vmjobadmin";
                        var password = configuration["WinRM:Password"] 
                            ?? Environment.GetEnvironmentVariable("VM_ADMIN_PASSWORD");

                        if (string.IsNullOrEmpty(password))
                        {
                            // Try to get from Kubernetes secret
                            try
                            {
                                var k8s = sp.GetRequiredService<IKubernetes>();
                                var secret = k8s.CoreV1.ReadNamespacedSecret("vmjob-credentials", "default");
                                
                                if (secret.Data.TryGetValue("password", out var passwordBytes))
                                {
                                    password = System.Text.Encoding.UTF8.GetString(passwordBytes);
                                    logger.LogInformation("Loaded VM credentials from Kubernetes secret");
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Could not read vmjob-credentials secret");
                            }
                        }

                        if (string.IsNullOrEmpty(password))
                        {
                            logger.LogError("VM admin password not configured! Set VM_ADMIN_PASSWORD environment variable or create vmjob-credentials secret");
                            throw new InvalidOperationException("VM credentials not configured");
                        }

                        return new VMCredentials { Username = username, Password = password };
                    });

                    // Register WinRM Connection Pool
                    services.AddSingleton<WinRMConnectionPool>();

                    // Register services
                    services.AddSingleton<IVMExecutor>((sp) =>
                    {
                        var pool = sp.GetRequiredService<WinRMConnectionPool>();
                        var logger = sp.GetRequiredService<ILogger<PooledVMExecutor>>();
                        
                        // Use pooled executor for better performance
                        return new PooledVMExecutor(pool, logger);
                    });
                    
                    // Register result aggregator
                    services.AddSingleton<ResultAggregator>();
                    
                    // Register the controller
                    services.AddHostedService<KubernetesStyleController>();
                    
                    Console.WriteLine("Using KubernetesStyleController with duration tracking");
                })
                .Build();

            Console.WriteLine("============================================================");
            Console.WriteLine("VMJob Controller Starting");
            Console.WriteLine($"Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine("============================================================");

            await host.RunAsync();
        }
    }

    internal class VMCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    /// <summary>
    /// VM Executor that uses connection pooling for better performance
    /// </summary>
    internal class PooledVMExecutor : IVMExecutor
    {
        private readonly WinRMConnectionPool _pool;
        private readonly ILogger<PooledVMExecutor> _logger;

        public PooledVMExecutor(WinRMConnectionPool pool, ILogger<PooledVMExecutor> logger)
        {
            _pool = pool;
            _logger = logger;
        }

        public async Task<ExecutionResult> ExecuteRemoteAsync(
            string host,
            int port,
            string command,
            List<string> args,
            string workingDir,
            List<EnvVar> envVars,
            TimeSpan timeout)
        {
            _logger.LogDebug("Executing command via connection pool on {Host}:{Port}", host, port);
            return await _pool.ExecuteAsync(host, command, args, workingDir, envVars, timeout);
        }
    }

}