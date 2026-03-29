using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Timers;
using NotchyWindows.Interop;

namespace NotchyWindows.Services;

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

    public void StartPolling() => _pollTimer.Start();
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

            // VS Code: "<file> - <folder> - Visual Studio Code"
            if (title.Contains(" - Visual Studio Code"))
            {
                var match = Regex.Match(title, @"(.+) - (.+) - Visual Studio Code");
                if (match.Success)
                {
                    var folder = match.Groups[2].Value.Trim();
                    projects.Add(new DetectedProject
                    {
                        Name = Path.GetFileName(folder),
                        Path = folder,
                        Ide = "VS Code"
                    });
                }
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

            return true;
        }, IntPtr.Zero);

        ProjectsDetected?.Invoke(projects);
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
