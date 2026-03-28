using System.Windows;
using System.Windows.Interop;

namespace NotchyWindows.Interop;

internal static class WindowHelper
{
    public static void MakeNonActivating(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }
}
