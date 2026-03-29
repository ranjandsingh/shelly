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
    }

    public static (uint? modifiers, uint? vk) LoadHotkey()
    {
        if (!File.Exists(FilePath)) return (null, null);
        try
        {
            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<Data>(json);
            return (data?.HotkeyModifiers, data?.HotkeyVk);
        }
        catch { return (null, null); }
    }

    public static void SaveHotkey(uint modifiers, uint vk)
    {
        var data = new Data { HotkeyModifiers = modifiers, HotkeyVk = vk };
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
    }

    public static void ClearHotkey()
    {
        var data = new Data();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
    }
}
