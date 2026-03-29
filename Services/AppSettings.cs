using System.IO;
using System.Text.Json;

namespace Shelly.Services;

public static class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Shelly", "settings.json");

    private class Data
    {
        public uint? HotkeyModifiers { get; set; }
        public uint? HotkeyVk { get; set; }
        public bool RememberSessions { get; set; } = true;
        public bool AutoCheckUpdates { get; set; } = true;
        public bool AutoLaunchClaude { get; set; } = true;
        public bool ShowHints { get; set; } = true;
        public int FontSize { get; set; } = 11;
        public string? DefaultShell { get; set; }
        public string? LastUpdateCheck { get; set; }
        public string? DismissedUpdateVersion { get; set; }
    }

    private static Data LoadData()
    {
        if (!File.Exists(FilePath)) return new Data();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Data>(json) ?? new Data();
        }
        catch { return new Data(); }
    }

    private static void SaveData(Data data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
    }

    public static (uint? modifiers, uint? vk) LoadHotkey()
    {
        var data = LoadData();
        return (data.HotkeyModifiers, data.HotkeyVk);
    }

    public static void SaveHotkey(uint modifiers, uint vk)
    {
        var data = LoadData();
        data.HotkeyModifiers = modifiers;
        data.HotkeyVk = vk;
        SaveData(data);
    }

    public static void ClearHotkey()
    {
        var data = LoadData();
        data.HotkeyModifiers = null;
        data.HotkeyVk = null;
        SaveData(data);
    }

    public static bool LoadRememberSessions()
    {
        return LoadData().RememberSessions;
    }

    public static void SaveRememberSessions(bool value)
    {
        var data = LoadData();
        data.RememberSessions = value;
        SaveData(data);
    }

    public static bool LoadAutoLaunchClaude() => LoadData().AutoLaunchClaude;

    public static void SaveAutoLaunchClaude(bool value)
    {
        var data = LoadData();
        data.AutoLaunchClaude = value;
        SaveData(data);
    }

    public static bool LoadShowHints() => LoadData().ShowHints;

    public static void SaveShowHints(bool value)
    {
        var data = LoadData();
        data.ShowHints = value;
        SaveData(data);
    }

    public static int LoadFontSize() => LoadData().FontSize;

    public static void SaveFontSize(int value)
    {
        var data = LoadData();
        data.FontSize = value;
        SaveData(data);
    }

    public static string? LoadDefaultShell() => LoadData().DefaultShell;

    public static void SaveDefaultShell(string? value)
    {
        var data = LoadData();
        data.DefaultShell = value;
        SaveData(data);
    }

    public static bool LoadAutoCheckUpdates() => LoadData().AutoCheckUpdates;

    public static void SaveAutoCheckUpdates(bool value)
    {
        var data = LoadData();
        data.AutoCheckUpdates = value;
        SaveData(data);
    }

    public static DateTime? LoadLastUpdateCheck()
    {
        var raw = LoadData().LastUpdateCheck;
        return raw != null && DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt : null;
    }

    public static void SaveLastUpdateCheck(DateTime value)
    {
        var data = LoadData();
        data.LastUpdateCheck = value.ToUniversalTime().ToString("o");
        SaveData(data);
    }

    public static string? LoadDismissedUpdateVersion() => LoadData().DismissedUpdateVersion;

    public static void SaveDismissedUpdateVersion(string? version)
    {
        var data = LoadData();
        data.DismissedUpdateVersion = version;
        SaveData(data);
    }
}
