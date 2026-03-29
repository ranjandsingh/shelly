using System.IO;

namespace Shelly.Services;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Shelly", "debug.log");

    private static readonly object Lock = new();

    static Logger()
    {
        var dir = Path.GetDirectoryName(LogPath)!;
        Directory.CreateDirectory(dir);
        // Clear log on startup
        File.WriteAllText(LogPath, $"=== Shelly started {DateTime.Now} ===\n");
    }

    public static void Log(string message)
    {
        lock (Lock)
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
    }
}
