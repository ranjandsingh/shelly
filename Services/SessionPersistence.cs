using System.IO;
using System.Text.Json;

namespace Shelly.Services;

public static class SessionPersistence
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Shelly", "sessions.json");

    public static void Save()
    {
        var data = SessionStore.Instance.Sessions
            .Where(s => s.ProjectPath != null)
            .Select(s => new SessionData
            {
                ProjectName = s.ProjectName,
                ProjectPath = s.ProjectPath!,
                WorkingDirectory = s.WorkingDirectory
            })
            .ToList();

        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
    }

    public static void Load()
    {
        if (!File.Exists(FilePath)) return;

        try
        {
            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<List<SessionData>>(json);
            if (data == null) return;

            foreach (var entry in data)
            {
                // Don't duplicate sessions that already exist (e.g., the default one)
                var exists = SessionStore.Instance.Sessions
                    .Any(s => string.Equals(s.ProjectPath, entry.ProjectPath, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                    SessionStore.Instance.AddSession(entry.ProjectName, entry.ProjectPath, entry.WorkingDirectory);
            }
        }
        catch
        {
            // Corrupted file — ignore and start fresh
        }
    }

    private class SessionData
    {
        public string ProjectName { get; set; } = "";
        public string ProjectPath { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
    }
}
