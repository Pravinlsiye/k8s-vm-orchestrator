using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace VMJobOrchestrator;

/// <summary>
/// Connection pool for WinRM connections to reduce overhead
/// Maintains persistent connections to VMs for faster execution
/// </summary>
public class WinRMConnectionPool : IDisposable
{
    private readonly ConcurrentDictionary<string, VMConnectionPool> _pools = new();
    private readonly ILogger<WinRMConnectionPool> _logger;
    private readonly Timer _healthCheckTimer;
    
    public WinRMConnectionPool(ILogger<WinRMConnectionPool> logger)
    {
        _logger = logger;
        
        // Start health check timer
        _healthCheckTimer = new Timer(
            callback: _ => Task.Run(HealthCheckAllPools),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(5)
        );
    }

    /// <summary>
    /// Execute a command using a pooled connection
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(
        string vmAddress,
        string command,
        List<string> args,
        string? workingDir,
        List<EnvVar>? envVars,
        TimeSpan timeout)
    {
        var pool = _pools.GetOrAdd(vmAddress, addr => new VMConnectionPool(addr, _logger));
        
        var startTime = DateTime.UtcNow;
        RentedConnection? connection = null;
        
        try
        {
            // Rent a connection from the pool
            connection = await pool.RentConnectionAsync(timeout);
            
            // Execute the command
            var result = await connection.ExecuteAsync(command, args, workingDir, envVars, timeout);
            
            // Record metrics
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command on {VM}", vmAddress);
            throw;
        }
        finally
        {
            // Return connection to pool
            if (connection != null)
            {
                pool.ReturnConnection(connection);
            }
        }
    }

    /// <summary>
    /// Individual VM connection pool
    /// </summary>
    private class VMConnectionPool
    {
        private readonly string _vmAddress;
        private readonly ILogger _logger;
        private readonly Channel<RentedConnection> _availableConnections;
        private readonly SemaphoreSlim _createConnectionSemaphore;
        private int _totalConnections = 0;
        private int _activeConnections = 0;
        
        private const int MinConnections = 2;
        private const int MaxConnections = 10;
        private const int MaxWaitQueueSize = 50;

        public VMConnectionPool(string vmAddress, ILogger logger)
        {
            _vmAddress = vmAddress;
            _logger = logger;
            _availableConnections = Channel.CreateBounded<RentedConnection>(
                new BoundedChannelOptions(MaxConnections)
                {
                    FullMode = BoundedChannelFullMode.Wait
                });
            _createConnectionSemaphore = new SemaphoreSlim(1, 1);
            
            // Pre-create minimum connections
            _ = Task.Run(async () => await WarmupConnections());
        }

        private async Task WarmupConnections()
        {
            for (int i = 0; i < MinConnections; i++)
            {
                try
                {
                    var connection = await CreateConnectionAsync();
                    await _availableConnections.Writer.WriteAsync(connection);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create warmup connection to {VM}", _vmAddress);
                }
            }
        }

        public async Task<RentedConnection> RentConnectionAsync(TimeSpan timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            
            // Try to get an available connection
            if (_availableConnections.Reader.TryRead(out var connection))
            {
                if (await connection.IsHealthyAsync())
                {
                    Interlocked.Increment(ref _activeConnections);
                    return connection;
                }
                else
                {
                    // Connection is unhealthy, dispose it
                    connection.Dispose();
                    Interlocked.Decrement(ref _totalConnections);
                }
            }

            // No healthy connection available, try to create new one
            if (_totalConnections < MaxConnections)
            {
                await _createConnectionSemaphore.WaitAsync(cts.Token);
                try
                {
                    if (_totalConnections < MaxConnections)
                    {
                        connection = await CreateConnectionAsync();
                        Interlocked.Increment(ref _totalConnections);
                        Interlocked.Increment(ref _activeConnections);
                        return connection;
                    }
                }
                finally
                {
                    _createConnectionSemaphore.Release();
                }
            }

            // Wait for a connection to become available
            connection = await _availableConnections.Reader.ReadAsync(cts.Token);
            
            // Health check before returning
            if (!await connection.IsHealthyAsync())
            {
                connection.Dispose();
                Interlocked.Decrement(ref _totalConnections);
                
                // Recursively try again
                return await RentConnectionAsync(timeout);
            }

            Interlocked.Increment(ref _activeConnections);
            return connection;
        }

        public void ReturnConnection(RentedConnection connection)
        {
            Interlocked.Decrement(ref _activeConnections);
            
            if (connection.IsValid && !connection.IsDisposed)
            {
                // Try to return to pool
                if (!_availableConnections.Writer.TryWrite(connection))
                {
                    // Pool is full, dispose extra connection
                    connection.Dispose();
                    Interlocked.Decrement(ref _totalConnections);
                }
            }
            else
            {
                // Connection is invalid, dispose it
                connection.Dispose();
                Interlocked.Decrement(ref _totalConnections);
            }
        }

        private async Task<RentedConnection> CreateConnectionAsync()
        {
            _logger.LogDebug("Creating new WinRM connection to {VM}", _vmAddress);
            
            // This would be replaced with actual WinRM connection logic
            // For now, we'll use the existing PythonWinRMExecutor
            var executor = new PythonWinRMExecutor(
                _logger as ILogger<PythonWinRMExecutor> ?? new Logger<PythonWinRMExecutor>(new LoggerFactory()),
                Environment.GetEnvironmentVariable("VM_ADMIN_USERNAME") ?? "vmjobadmin",
                Environment.GetEnvironmentVariable("VM_ADMIN_PASSWORD") ?? throw new InvalidOperationException("VM_ADMIN_PASSWORD environment variable not set")
            );
            
            return new RentedConnection(Guid.NewGuid().ToString(), _vmAddress, executor);
        }

        public async Task<PoolHealth> GetHealthAsync()
        {
            return new PoolHealth
            {
                VMAddress = _vmAddress,
                TotalConnections = _totalConnections,
                ActiveConnections = _activeConnections,
                AvailableConnections = _availableConnections.Reader.Count,
                IsHealthy = _totalConnections > 0
            };
        }
    }

    /// <summary>
    /// Represents a rented connection from the pool
    /// </summary>
    public class RentedConnection : IDisposable
    {
        public string ConnectionId { get; }
        public string VMAddress { get; }
        public bool IsValid { get; private set; } = true;
        public bool IsDisposed { get; private set; }
        
        private readonly IVMExecutor _executor;
        private DateTime _lastUsed = DateTime.UtcNow;

        public RentedConnection(string connectionId, string vmAddress, IVMExecutor executor)
        {
            ConnectionId = connectionId;
            VMAddress = vmAddress;
            _executor = executor;
        }

        public async Task<ExecutionResult> ExecuteAsync(
            string command, 
            List<string> args,
            string? workingDir,
            List<EnvVar>? envVars,
            TimeSpan timeout)
        {
            _lastUsed = DateTime.UtcNow;
            return await _executor.ExecuteRemoteAsync(
                VMAddress, 5985, command, args, workingDir, envVars, timeout);
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                // Simple health check - echo command
                var result = await _executor.ExecuteRemoteAsync(
                    VMAddress, 5985, "echo", new List<string> { "health" }, 
                    null, null, TimeSpan.FromSeconds(5));
                
                return result.Success && result.ExitCode == 0;
            }
            catch
            {
                IsValid = false;
                return false;
            }
        }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;
                IsValid = false;
                // Dispose underlying resources if needed
            }
        }
    }


    private async Task HealthCheckAllPools()
    {
        foreach (var pool in _pools.Values)
        {
            try
            {
                var health = await pool.GetHealthAsync();
                _logger.LogDebug("Pool health for {VM}: {Health}", 
                    health.VMAddress, health);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
            }
        }
    }

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        
        foreach (var pool in _pools.Values)
        {
            // Dispose all connections in pools
        }
    }


    public class PoolHealth
    {
        public string VMAddress { get; set; }
        public int TotalConnections { get; set; }
        public int ActiveConnections { get; set; }
        public int AvailableConnections { get; set; }
        public bool IsHealthy { get; set; }
    }
}
