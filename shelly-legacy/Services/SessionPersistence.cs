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
        var store = SessionStore.Instance;
        var activeIndex = store.ActiveSession != null
            ? store.Sessions.IndexOf(store.ActiveSession)
            : 0;

        var state = new SavedState
        {
            ActiveIndex = activeIndex,
            Sessions = store.Sessions
                .Select(s => new SessionData
                {
                    ProjectName = s.ProjectName,
                    ProjectPath = s.ProjectPath,
                    WorkingDirectory = s.WorkingDirectory
                })
                .ToList()
        };

        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
    }

    /// <summary>
    /// Loads saved sessions into the SessionStore, replacing any existing sessions.
    /// Returns true if sessions were restored.
    /// </summary>
    public static bool Load()
    {
        if (!File.Exists(FilePath)) return false;

        try
        {
            var json = File.ReadAllText(FilePath);
            var state = JsonSerializer.Deserialize<SavedState>(json);
            if (state?.Sessions == null || state.Sessions.Count == 0) return false;

            var store = SessionStore.Instance;
            var activeIndex = state.ActiveIndex >= 0 && state.ActiveIndex < state.Sessions.Count
                ? state.ActiveIndex
                : 0;

            for (int i = 0; i < state.Sessions.Count; i++)
            {
                var entry = state.Sessions[i];
                var session = store.AddSession(entry.ProjectName, entry.ProjectPath, entry.WorkingDirectory);

                // Only the active session should auto-launch claude on first attach
                if (i != activeIndex)
                    session.SkipAutoLaunch = true;
            }

            // Restore active session
            store.SelectSession(store.Sessions[activeIndex].Id);

            return true;
        }
        catch
        {
            // Corrupted file — ignore and start fresh
            return false;
        }
    }

    public static void Delete()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }

    private class SavedState
    {
        public int ActiveIndex { get; set; }
        public List<SessionData> Sessions { get; set; } = new();
    }

    private class SessionData
    {
        public string ProjectName { get; set; } = "";
        public string? ProjectPath { get; set; }
        public string WorkingDirectory { get; set; } = "";
    }
}
