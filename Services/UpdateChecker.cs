using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Shelly.Models;

namespace Shelly.Services;

public enum UpdateDownloadState { None, Downloading, Ready, Failed }

public static class UpdateChecker
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Shelly-UpdateChecker" },
            { "Accept", "application/vnd.github+json" }
        }
    };

    private const string ReleasesUrl =
        "https://api.github.com/repos/ranjandsingh/shelly/releases/latest";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private static readonly string[] TrustedHosts = ["github.com", "githubusercontent.com"];

    private static volatile UpdateInfo? _latestUpdate;
    public static UpdateInfo? LatestUpdate => _latestUpdate;

    private static volatile UpdateDownloadState _downloadState = UpdateDownloadState.None;
    public static UpdateDownloadState DownloadState => _downloadState;

    private static string? _downloadedInstallerPath;

    public static event Action? UpdateAvailable;

    public static async Task<UpdateInfo?> CheckForUpdateAsync(bool force = false)
    {
        try
        {
            if (!force && !AppSettings.LoadAutoCheckUpdates())
                return null;

            if (!force)
            {
                var lastCheck = AppSettings.LoadLastUpdateCheck();
                if (lastCheck.HasValue && DateTime.UtcNow - lastCheck.Value < CheckInterval)
                    return _latestUpdate;
            }

            var json = await Http.GetStringAsync(ReleasesUrl);

            string tagName, htmlUrl, body;
            string? installerUrl = null;
            Version? remoteVersion;

            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;

                tagName = root.GetProperty("tag_name").GetString() ?? "";
                htmlUrl = root.GetProperty("html_url").GetString() ?? "";
                body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";

                var versionStr = tagName.TrimStart('v');
                if (!Version.TryParse(versionStr, out remoteVersion))
                    return null;

                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            installerUrl = asset.GetProperty("browser_download_url").GetString();
                            break;
                        }
                    }
                }
            }

            AppSettings.SaveLastUpdateCheck(DateTime.UtcNow);

            var localVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (localVersion == null || remoteVersion <= localVersion)
            {
                _latestUpdate = null;
                return null;
            }

            if (!force)
            {
                var dismissed = AppSettings.LoadDismissedUpdateVersion();
                if (dismissed == tagName.TrimStart('v'))
                    return null;
            }

            var info = new UpdateInfo(tagName, remoteVersion, htmlUrl, installerUrl, body);
            var previous = _latestUpdate;
            _latestUpdate = info;
            if (previous?.TagName != info.TagName)
                UpdateAvailable?.Invoke();

            Logger.Log($"Update available: {tagName}");

            // Start background download of the installer
            _ = Task.Run(() => DownloadInstallerAsync(info));

            return info;
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Download the installer in the background so it's ready when the user wants to install.</summary>
    private static async Task DownloadInstallerAsync(UpdateInfo info)
    {
        if (info.InstallerUrl == null || !IsTrustedUrl(info.InstallerUrl))
        {
            _downloadState = UpdateDownloadState.Failed;
            return;
        }

        // Skip if already downloaded for this version
        var tempPath = Path.Combine(Path.GetTempPath(), $"Shelly-{info.TagName}-setup.exe");
        if (File.Exists(tempPath))
        {
            _downloadedInstallerPath = tempPath;
            _downloadState = UpdateDownloadState.Ready;
            Logger.Log($"Installer already downloaded: {tempPath}");
            return;
        }

        _downloadState = UpdateDownloadState.Downloading;
        Logger.Log($"Downloading installer in background: {info.InstallerUrl}");

        try
        {
            using var downloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            downloadClient.DefaultRequestHeaders.Add("User-Agent", "Shelly-UpdateChecker");

            using var response = await downloadClient.GetAsync(info.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var partialPath = tempPath + ".partial";
            await using (var fs = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }

            // Rename to final path only after complete download
            File.Move(partialPath, tempPath, overwrite: true);

            _downloadedInstallerPath = tempPath;
            _downloadState = UpdateDownloadState.Ready;
            Logger.Log($"Installer downloaded: {tempPath}");
        }
        catch (Exception ex)
        {
            _downloadState = UpdateDownloadState.Failed;
            Logger.Log($"Background installer download failed: {ex.Message}");
        }
    }

    public static bool IsInstallerEdition()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        return dir != null && File.Exists(Path.Combine(dir, "unins000.exe"));
    }

    /// <summary>Install the update if downloaded, otherwise open the release page.</summary>
    public static Task ApplyUpdateAsync(UpdateInfo info)
    {
        // If installer is ready, launch it
        if (_downloadState == UpdateDownloadState.Ready &&
            _downloadedInstallerPath != null &&
            File.Exists(_downloadedInstallerPath))
        {
            try
            {
                Logger.Log($"Launching downloaded installer: {_downloadedInstallerPath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = _downloadedInstallerPath,
                    Arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS",
                    UseShellExecute = true
                });

                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => System.Windows.Application.Current.Shutdown());
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.Log($"Launching installer failed: {ex.Message}");
            }
        }

        // Fallback: open release page
        OpenReleasePage(info);
        return Task.CompletedTask;
    }

    private static bool IsTrustedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return Array.Exists(TrustedHosts, host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    private static void OpenReleasePage(UpdateInfo info)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = info.HtmlUrl,
            UseShellExecute = true
        });
    }
}
