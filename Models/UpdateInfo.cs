namespace Shelly.Models;

public record UpdateInfo(
    string TagName,
    Version Version,
    string HtmlUrl,
    string? InstallerUrl,
    string ReleaseNotes
);
