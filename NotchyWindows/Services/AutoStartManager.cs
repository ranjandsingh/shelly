using Microsoft.Win32;

namespace NotchyWindows.Services;

public static class AutoStartManager
{
    private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "NotchyWindows";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
    }

    public static void Enable()
    {
        var exePath = Environment.ProcessPath;
        if (exePath == null) return;

        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
        key?.SetValue(AppName, $"\"{exePath}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    public static void Toggle()
    {
        if (IsEnabled)
            Disable();
        else
            Enable();
    }
}
