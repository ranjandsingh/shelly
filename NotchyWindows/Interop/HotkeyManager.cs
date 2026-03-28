using System.Windows;
using System.Windows.Interop;

namespace NotchyWindows.Interop;

internal class HotkeyManager
{
    private const int HOTKEY_ID = 1;
    private HwndSource? _source;

    public event Action? HotkeyPressed;

    public void Register()
    {
        var helper = new WindowInteropHelper(Application.Current.MainWindow ?? new Window());
        // We need a message-only window for the hotkey since we have no main window
        var msgWindow = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false };
        msgWindow.Show();
        msgWindow.Hide();

        var hwnd = new WindowInteropHelper(msgWindow).Handle;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        NativeMethods.RegisterHotKey(hwnd, HOTKEY_ID,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_OEM_3);
    }

    public void Unregister()
    {
        if (_source != null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HOTKEY_ID);
            _source.RemoveHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
