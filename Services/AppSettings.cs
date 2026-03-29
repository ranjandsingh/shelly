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
}
