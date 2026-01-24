using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ACT.Services
{
    public interface IRScriptRunner
    {
        Task<RProcessResult> RunAsync(string scriptFileNameOrPath, string[] args, CancellationToken ct = default);
        
        Task<RProcessResult> RunStringAsync(string rCode, string[] args = null, CancellationToken ct = default);

        Task<RProcessResult<T>> RunJsonAsync<T>(
            string scriptFileNameOrPath,
            string[] args,
            JsonSerializerOptions? jsonOptions = null,
            CancellationToken ct = default);
    }

    public sealed class RScriptRunner : IRScriptRunner
    {
        private const string EnvRscriptPath = "RSCRIPT_PATH";
        private const string EnvScriptsDir = "R_SCRIPTS_DIR";

        private readonly ILogger<RScriptRunner> _logger;

        // Singleton-freundlich: nur readonly dependencies, kein mutable state
        public RScriptRunner(ILogger<RScriptRunner> logger)
        {
            _logger = logger;
        }

        public Task<RProcessResult> RunStringAsync(string rCode, string[] args = null, CancellationToken ct = default)
        {
            var rscriptExe = ResolveRscriptExecutable();

            // Prepare arguments: -e "rCode" arg1 arg2 ...
            var cmdArgs = new StringBuilder();
            cmdArgs.Append("-e ");
            
            static string Q(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
            
            cmdArgs.Append(Q(rCode));

            if (args != null)
            {
                foreach (var a in args)
                    cmdArgs.Append(' ').Append(Q(a ?? string.Empty));
            }

            return RunProcessAsync(rscriptExe, cmdArgs.ToString(), ct);
        }

        public Task<RProcessResult> RunAsync(string scriptFileNameOrPath, string[] args, CancellationToken ct = default)
        {
            var rscriptExe = ResolveRscriptExecutable();
            var scriptPath = ResolveScriptPath(scriptFileNameOrPath);

            static string Q(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

            var cmdArgs = new StringBuilder();
            cmdArgs.Append(Q(scriptPath));
            if (args != null)
            {
                foreach (var a in args)
                    cmdArgs.Append(' ').Append(Q(a ?? string.Empty));
            }

            // Set working directory to script directory for file-based runs
            var workingDir = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory;

            return RunProcessAsync(rscriptExe, cmdArgs.ToString(), ct, workingDir);
        }

        private async Task<RProcessResult> RunProcessAsync(string rscriptExe, string arguments, CancellationToken ct, string workingDirectory = null)
        {
             var psi = new ProcessStartInfo
            {
                FileName = rscriptExe,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
            };

            var commandLine = $"{rscriptExe} {psi.Arguments}";
            _logger.LogDebug("Starting R process: {CommandLine}", commandLine);

            var sw = Stopwatch.StartNew();
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            using var reg = ct.Register(() =>
            {
                try
                {
                    if (!p.HasExited)
                    {
                        _logger.LogWarning("Cancellation requested. Killing R process tree. Command: {CommandLine}", commandLine);
                        p.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to kill R process. Command: {CommandLine}", commandLine);
                }
            });

            p.Start();

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            sw.Stop();

            var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
            var stderr = (await stderrTask.ConfigureAwait(false)).Trim();

            var result = new RProcessResult(
                exitCode: p.ExitCode,
                stdOut: stdout,
                stdErr: stderr,
                duration: sw.Elapsed,
                commandLine: commandLine
            );

            if (!result.IsSuccess)
            {
                _logger.LogWarning("R process failed (ExitCode={ExitCode}). StdErr: {StdErr}. Command: {CommandLine}",
                    result.ExitCode, result.StdErr, result.CommandLine);
            }

            return result;
        }

        public async Task<RProcessResult<T>> RunJsonAsync<T>(
            string scriptFileNameOrPath,
            string[] args,
            JsonSerializerOptions jsonOptions = null,
            CancellationToken ct = default)
        {
            var proc = await RunAsync(scriptFileNameOrPath, args, ct).ConfigureAwait(false);

            if (!proc.IsSuccess)
            {
                // Deine Policy: entweder Exception oder Payload default.
                // Hier: Exception, weil Prozess schon fehlgeschlagen ist.
                throw new InvalidOperationException(
                    $"R failed (ExitCode={proc.ExitCode}). StdErr: {proc.StdErr}. Command: {proc.CommandLine}");
            }

            if (string.IsNullOrWhiteSpace(proc.StdOut))
                throw new InvalidOperationException($"R returned empty stdout. Command: {proc.CommandLine}");

            try
            {
                var payload = JsonSerializer.Deserialize<T>(proc.StdOut, jsonOptions);
                if (payload is null)
                    throw new InvalidOperationException($"R returned JSON but deserialized to null. StdOut: {proc.StdOut}");

                return new RProcessResult<T>(proc, payload);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"R returned non-parseable JSON. Command: {proc.CommandLine}. StdOut: {proc.StdOut}",
                    ex);
            }
        }

        private static string ResolveRscriptExecutable()
        {
            var rscript = Environment.GetEnvironmentVariable(EnvRscriptPath);

            // Wenn du keinen PATH-Fallback willst: hier stattdessen throw.
            if (string.IsNullOrWhiteSpace(rscript))
                return "Rscript";

            return rscript;
        }

        private static string ResolveScriptPath(string scriptFileNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(scriptFileNameOrPath))
                throw new ArgumentException("Script path must not be empty.", nameof(scriptFileNameOrPath));

            if (File.Exists(scriptFileNameOrPath))
                return Path.GetFullPath(scriptFileNameOrPath);

            var dir = Environment.GetEnvironmentVariable(EnvScriptsDir);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                var candidate = Path.Combine(dir, scriptFileNameOrPath);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            var baseCandidate = Path.Combine(AppContext.BaseDirectory, scriptFileNameOrPath);
            if (File.Exists(baseCandidate))
                return Path.GetFullPath(baseCandidate);

            throw new FileNotFoundException(
                $"R script not found. Tried: '{scriptFileNameOrPath}', R_SCRIPTS_DIR, AppContext.BaseDirectory.",
                scriptFileNameOrPath);
        }
    }

    public sealed class RProcessResult
    {
        public int ExitCode { get; }
        public string StdOut { get; }
        public string StdErr { get; }
        public TimeSpan Duration { get; }
        public string CommandLine { get; }

        public bool IsSuccess => ExitCode == 0;

        public RProcessResult(int exitCode, string stdOut, string stdErr, TimeSpan duration, string commandLine)
        {
            ExitCode = exitCode;
            StdOut = stdOut ?? string.Empty;
            StdErr = stdErr ?? string.Empty;
            Duration = duration;
            CommandLine = commandLine ?? string.Empty;
        }
    }

    public sealed class RProcessResult<T>
    {
        public RProcessResult Process { get; }
        public T Payload { get; }

        public bool IsSuccess => Process.IsSuccess;

        public RProcessResult(RProcessResult process, T payload)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
            Payload = payload;
        }
    }

    public static class RScriptRunnerDI
    {
        public static void AddRScriptRunner(this IServiceCollection services)
        {
            services.AddScoped<IRScriptRunner, RScriptRunner>();
        }
    }
}
