using System.Diagnostics;
using System.IO;

namespace NotchyWindows.Services;

public static class CheckpointManager
{
    public static async Task<bool> CreateCheckpoint(string projectPath, string projectName)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var refName = $"refs/notchy-snapshots/{projectName}/{timestamp}";

        var tempIndex = Path.Combine(Path.GetTempPath(), $"notchy-index-{Guid.NewGuid()}");

        try
        {
            // Copy current index
            var gitDir = await RunGit(projectPath, "rev-parse --git-dir");
            if (gitDir == null) return false;

            var currentIndex = Path.Combine(projectPath, gitDir.Trim(), "index");
            if (File.Exists(currentIndex))
                File.Copy(currentIndex, tempIndex, true);

            // Add all files using temp index
            var env = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = tempIndex };
            await RunGit(projectPath, "add -A", env);

            // Write tree
            var treeHash = await RunGit(projectPath, "write-tree", env);
            if (string.IsNullOrWhiteSpace(treeHash)) return false;

            // Create commit
            var commitHash = await RunGit(projectPath,
                $"commit-tree {treeHash.Trim()} -m \"Notchy checkpoint {timestamp}\"", env);
            if (string.IsNullOrWhiteSpace(commitHash)) return false;

            // Update ref
            await RunGit(projectPath, $"update-ref {refName} {commitHash.Trim()}");
            return true;
        }
        finally
        {
            if (File.Exists(tempIndex))
                File.Delete(tempIndex);
        }
    }

    public static async Task<List<CheckpointEntry>> ListCheckpoints(string projectPath, string projectName)
    {
        var output = await RunGit(projectPath,
            $"for-each-ref --format=%(refname):%(objectname):%(creatordate:iso8601) refs/notchy-snapshots/{projectName}/");

        if (string.IsNullOrWhiteSpace(output))
            return new List<CheckpointEntry>();

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split(':');
                return new CheckpointEntry
                {
                    Ref = parts[0],
                    Hash = parts.Length > 1 ? parts[1] : "",
                    Date = parts.Length > 2 ? parts[2] : ""
                };
            })
            .OrderByDescending(e => e.Date)
            .ToList();
    }

    public static async Task<bool> RestoreCheckpoint(string projectPath, string commitHash)
    {
        var result = await RunGit(projectPath, $"checkout {commitHash} -- .");
        return result != null;
    }

    private static async Task<string?> RunGit(string workingDir, string arguments,
        Dictionary<string, string>? envVars = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (envVars != null)
        {
            foreach (var kv in envVars)
                psi.Environment[kv.Key] = kv.Value;
        }

        try
        {
            var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}

public class CheckpointEntry
{
    public required string Ref { get; init; }
    public required string Hash { get; init; }
    public required string Date { get; init; }
}
