using System.Windows;
using System.Windows.Interop;

namespace NotchyWindows.Interop;

public class HotkeyManager
{
    private const int DEFAULT_HOTKEY_ID = 1;
    private const int CUSTOM_HOTKEY_ID = 2;
    private HwndSource? _source;
    private IntPtr _hwnd;

    /// <summary>The custom hotkey modifier flags (MOD_CONTROL, MOD_ALT, etc.). Null if no custom hotkey.</summary>
    public uint? CustomModifiers { get; private set; }
    /// <summary>The custom hotkey virtual key code. Null if no custom hotkey.</summary>
    public uint? CustomVk { get; private set; }

    public event Action? HotkeyPressed;

    public void Register()
    {
        var msgWindow = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false };
        msgWindow.Show();
        msgWindow.Hide();

        _hwnd = new WindowInteropHelper(msgWindow).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        // Default: Ctrl+`
        NativeMethods.RegisterHotKey(_hwnd, DEFAULT_HOTKEY_ID,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_OEM_3);

        // Load and register saved custom hotkey
        var (mod, vk) = Services.AppSettings.LoadHotkey();
        if (mod.HasValue && vk.HasValue)
            SetCustomHotkey(mod.Value, vk.Value);
    }

    public void SetCustomHotkey(uint modifiers, uint vk)
    {
        // Unregister previous custom hotkey if any
        if (CustomVk.HasValue)
            NativeMethods.UnregisterHotKey(_hwnd, CUSTOM_HOTKEY_ID);

        CustomModifiers = modifiers;
        CustomVk = vk;

        NativeMethods.RegisterHotKey(_hwnd, CUSTOM_HOTKEY_ID,
            modifiers | NativeMethods.MOD_NOREPEAT, vk);

        Services.AppSettings.SaveHotkey(modifiers, vk);
    }

    public void ClearCustomHotkey()
    {
        if (CustomVk.HasValue)
        {
            NativeMethods.UnregisterHotKey(_hwnd, CUSTOM_HOTKEY_ID);
            CustomModifiers = null;
            CustomVk = null;
            Services.AppSettings.ClearHotkey();
        }
    }

    public void Unregister()
    {
        if (_source != null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, DEFAULT_HOTKEY_ID);
            if (CustomVk.HasValue)
                NativeMethods.UnregisterHotKey(_source.Handle, CUSTOM_HOTKEY_ID);
            _source.RemoveHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == DEFAULT_HOTKEY_ID || id == CUSTOM_HOTKEY_ID)
            {
                HotkeyPressed?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Format a modifier+vk combo as a readable string like "Ctrl+Shift+A".</summary>
    public static string FormatHotkey(uint modifiers, uint vk)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(VkToString(vk));
        return string.Join("+", parts);
    }

    private static string VkToString(uint vk)
    {
        // Letters A-Z
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        // Numbers 0-9
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        // Function keys
        if (vk >= 0x70 && vk <= 0x87) return $"F{vk - 0x6F}";
        // Common keys
        return vk switch
        {
            0xC0 => "`",
            0xBD => "-",
            0xBB => "=",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xBA => ";",
            0xDE => "'",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            0x20 => "Space",
            0x09 => "Tab",
            0x1B => "Esc",
            0x0D => "Enter",
            _ => $"0x{vk:X2}"
        };
    }
}
