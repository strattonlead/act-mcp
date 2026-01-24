using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ACT.Services;

public interface IREnvironmentService
{
    Task<(bool IsAvailable, string Version)> GetRInfoAsync(CancellationToken ct = default);
    Task<List<(string PackageName, bool IsInstalled)>> GetPackagesStatusAsync(CancellationToken ct = default);
}

public class REnvironmentService : IREnvironmentService
{
    private const string EnvRscriptPath = "RSCRIPT_PATH";
    private readonly ILogger<REnvironmentService> _logger;
    private readonly IRScriptRunner _rRunner;

    public REnvironmentService(ILogger<REnvironmentService> logger, IRScriptRunner rRunner)
    {
        _logger = logger;
        _rRunner = rRunner;
    }

    public async Task<(bool IsAvailable, string Version)> GetRInfoAsync(CancellationToken ct = default)
    {
        var rscriptExe = ResolveRscriptExecutable();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = rscriptExe,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = new Process { StartInfo = psi };
            p.Start();

            var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = p.StandardError.ReadToEndAsync(ct);

            await p.WaitForExitAsync(ct);

            var output = (await stdoutTask) + (await stderrTask);
            
            if (p.ExitCode == 0)
            {
                var match = Regex.Match(output, @"version\s+(\d+\.\d+\.\d+)");
                if (match.Success)
                {
                    return (true, match.Groups[1].Value);
                }
                return (true, "Unknown Version");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check R environment.");
        }

        return (false, string.Empty);
    }

    public async Task<List<(string PackageName, bool IsInstalled)>> GetPackagesStatusAsync(CancellationToken ct = default)
    {
        var packagesToCheck = new[] { "remotes", "jsonlite", "Rcpp", "devtools", "actdata", "bayesactR", "inteRact" };
        var results = new List<(string, bool)>();

        // Construct R code to check packages
        // We will output: "pkg:TRUE|FALSE" per line
        var rCode = string.Join("\n", packagesToCheck.Select(p => $"cat(sprintf('{p}:%s\\n', requireNamespace('{p}', quietly=TRUE)))"));
        
        try 
        {
             var result = await _rRunner.RunStringAsync(rCode, ct: ct);
             if (result.IsSuccess)
             {
                 var lines = result.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                 foreach (var line in lines)
                 {
                     var parts = line.Split(':');
                     if (parts.Length == 2)
                     {
                         results.Add((parts[0].Trim(), parts[1].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase)));
                     }
                 }
             }
             else
             {
                 _logger.LogWarning("R package check failed: {StdErr}", result.StdErr);
             }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception running package check");
        }

        // Fill in missing packages as false if something went wrong
        foreach (var pkg in packagesToCheck)
        {
            if (!results.Any(r => r.Item1 == pkg))
            {
                results.Add((pkg, false));
            }
        }
        
        return results;
    }

    private static string ResolveRscriptExecutable()
    {
        var rscript = Environment.GetEnvironmentVariable(EnvRscriptPath);
        if (string.IsNullOrWhiteSpace(rscript))
            return "Rscript";
        return rscript;
    }
}
