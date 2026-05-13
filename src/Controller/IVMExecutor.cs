using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VMJobOrchestrator
{
    /// <summary>
    /// Interface for executing commands on remote VMs
    /// </summary>
    public interface IVMExecutor
    {
        /// <summary>
        /// Executes a command on a remote VM
        /// </summary>
        Task<ExecutionResult> ExecuteRemoteAsync(
            string host,
            int port,
            string command,
            List<string> args,
            string workingDir,
            List<EnvVar> envVars,
            TimeSpan timeout);
    }
    
    /// <summary>
    /// Environment variable
    /// </summary>
    public class EnvVar
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
