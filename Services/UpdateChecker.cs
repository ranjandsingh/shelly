using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Shelly.Models;

namespace Shelly.Services;

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
            return info;
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check failed: {ex.Message}");
            return null;
        }
    }

    public static bool IsInstallerEdition()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        return dir != null && File.Exists(Path.Combine(dir, "unins000.exe"));
    }

    public static async Task ApplyUpdateAsync(UpdateInfo info)
    {
        if (IsInstallerEdition() && info.InstallerUrl != null)
        {
            try
            {
                if (!IsTrustedUrl(info.InstallerUrl))
                {
                    Logger.Log($"Installer URL not from trusted host, opening release page instead");
                    OpenReleasePage(info);
                    return;
                }

                var tempPath = Path.Combine(Path.GetTempPath(), $"Shelly-{info.TagName}-setup.exe");

                Logger.Log($"Downloading installer to {tempPath}");
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var response = await Http.GetAsync(info.InstallerUrl,
                    HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);

                Logger.Log("Launching installer");
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    Arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS",
                    UseShellExecute = true
                });

                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => System.Windows.Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                Logger.Log($"Update download/install failed: {ex.Message}");
                OpenReleasePage(info);
            }
        }
        else
        {
            OpenReleasePage(info);
        }
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
