using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Timers;
using Shelly.Interop;

namespace Shelly.Services;

public class IdeDetector
{
    public static IdeDetector Instance { get; } = new();

    private readonly System.Timers.Timer _pollTimer;

    public event Action<List<DetectedProject>>? ProjectsDetected;

    private IdeDetector()
    {
        _pollTimer = new System.Timers.Timer(5000) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) => Detect();
    }

    public void StartPolling()
    {
        if (!_pollTimer.Enabled)
        {
            Detect(); // run immediately on first start
            _pollTimer.Start();
        }
    }
    public void StopPolling() => _pollTimer.Stop();

    public void Detect()
    {
        var projects = new List<DetectedProject>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            int length = NativeMethods.GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            // VS Code-family: "<file> - <folder> - <IDE name>"
            // Covers VS Code, Cursor, Windsurf
            var vscodeFamily = MatchVsCodeFamily(title);
            if (vscodeFamily != null)
            {
                projects.Add(vscodeFamily);
            }
            // JetBrains: "<project> – <IDE name>"
            else if (title.Contains(" \u2013 ") && IsJetBrainsIde(title))
            {
                var parts = title.Split(" \u2013 ");
                if (parts.Length >= 2)
                {
                    projects.Add(new DetectedProject
                    {
                        Name = parts[0].Trim(),
                        Path = null, // JetBrains doesn't expose path in title
                        Ide = parts[^1].Trim()
                    });
                }
            }
            // Zed: "<file> - <folder> - Zed" or "<folder> - Zed"
            else if (title.EndsWith(" - Zed"))
            {
                var match = Regex.Match(title, @"(.+) - (.+) - Zed$");
                if (match.Success)
                {
                    var folder = match.Groups[2].Value.Trim();
                    projects.Add(new DetectedProject
                    {
                        Name = Path.GetFileName(folder),
                        Path = folder,
                        Ide = "Zed"
                    });
                }
                else
                {
                    var folder = title[..^" - Zed".Length].Trim();
                    if (folder.Length > 0)
                    {
                        projects.Add(new DetectedProject
                        {
                            Name = Path.GetFileName(folder),
                            Path = folder,
                            Ide = "Zed"
                        });
                    }
                }
            }
            // Visual Studio: "<solution/project> - Microsoft Visual Studio"
            else if (title.EndsWith(" - Microsoft Visual Studio"))
            {
                var name = title[..^" - Microsoft Visual Studio".Length].Trim();
                if (name.Length > 0)
                {
                    projects.Add(new DetectedProject
                    {
                        Name = name,
                        Path = null, // VS doesn't expose full path in title
                        Ide = "Visual Studio"
                    });
                }
            }
            // Sublime Text: "<file> — <folder> - Sublime Text" or "<folder> - Sublime Text"
            else if (title.EndsWith(" - Sublime Text"))
            {
                var content = title[..^" - Sublime Text".Length].Trim();
                // Try to extract folder from "file — folder" pattern (em dash)
                var dashIdx = content.LastIndexOf(" \u2014 ");
                var folder = dashIdx >= 0 ? content[(dashIdx + 3)..].Trim() : content;
                if (folder.Length > 0)
                {
                    projects.Add(new DetectedProject
                    {
                        Name = Path.GetFileName(folder),
                        Path = folder,
                        Ide = "Sublime Text"
                    });
                }
            }

            return true;
        }, IntPtr.Zero);

        ProjectsDetected?.Invoke(projects);
    }

    private static readonly (string marker, string label)[] VsCodeFamilyIdes =
    {
        (" - Visual Studio Code", "VS Code"),
        (" - Cursor", "Cursor"),
        (" - Windsurf", "Windsurf"),
    };

    private static DetectedProject? MatchVsCodeFamily(string title)
    {
        foreach (var (marker, label) in VsCodeFamilyIdes)
        {
            // IDE name may not be at the end — e.g. "file - folder - Cursor - Untracked"
            var idx = title.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) continue;
            var content = title[..idx];
            // Pattern: "<file> - <folder>" or just "<folder>"
            var dashIdx = content.LastIndexOf(" - ");
            var folder = dashIdx >= 0 ? content[(dashIdx + 3)..].Trim() : content.Trim();
            if (folder.Length == 0) continue;
            return new DetectedProject
            {
                Name = Path.GetFileName(folder),
                Path = folder,
                Ide = label
            };
        }
        return null;
    }

    private static bool IsJetBrainsIde(string title)
    {
        string[] ideNames = { "IntelliJ IDEA", "WebStorm", "Rider", "PyCharm", "PhpStorm", "GoLand", "CLion", "RubyMine", "DataGrip" };
        return ideNames.Any(ide => title.Contains(ide, StringComparison.OrdinalIgnoreCase));
    }
}

public class DetectedProject
{
    public required string Name { get; init; }
    public string? Path { get; init; }
    public required string Ide { get; init; }
}
