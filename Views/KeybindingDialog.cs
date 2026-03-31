using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shelly.Interop;

namespace Shelly.Views;

/// <summary>
/// Modal dialog for capturing a custom keyboard shortcut.
/// Extracted from SessionTabBar.
/// </summary>
public static class KeybindingDialog
{
    public static void Show(HotkeyManager hkMgr)
    {
        uint capturedMod = 0;
        uint capturedVk = 0;

        var dialog = new Window
        {
            Title = "Set Keybinding",
            Width = 320, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            BorderThickness = new Thickness(1),
            Topmost = true
        };

        var panel = new StackPanel { Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Center };

        panel.Children.Add(new TextBlock
        {
            Text = "Press Ctrl + key (or Ctrl+Shift, Ctrl+Alt, etc.)",
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
            FontSize = 12, Margin = new Thickness(0, 0, 0, 10),
            TextAlignment = TextAlignment.Center
        });

        var display = new TextBlock
        {
            Text = "Waiting for keypress...",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(display);

        panel.Children.Add(new TextBlock
        {
            Text = "Press Escape to cancel",
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center
        });

        dialog.Content = panel;

        dialog.KeyDown += (_, ke) =>
        {
            ke.Handled = true;

            if (ke.Key == Key.Escape) { dialog.Close(); return; }

            if (ke.Key == Key.LeftCtrl || ke.Key == Key.RightCtrl ||
                ke.Key == Key.LeftAlt || ke.Key == Key.RightAlt ||
                ke.Key == Key.LeftShift || ke.Key == Key.RightShift ||
                ke.Key == Key.LWin || ke.Key == Key.RWin ||
                ke.Key == Key.System && (ke.SystemKey == Key.LeftAlt || ke.SystemKey == Key.RightAlt))
                return;

            var mod = Keyboard.Modifiers;
            if (!mod.HasFlag(ModifierKeys.Control)) return;

            uint nativeMod = NativeMethods.MOD_CONTROL;
            if (mod.HasFlag(ModifierKeys.Alt)) nativeMod |= NativeMethods.MOD_ALT;
            if (mod.HasFlag(ModifierKeys.Shift)) nativeMod |= NativeMethods.MOD_SHIFT;

            var actualKey = ke.Key == Key.System ? ke.SystemKey : ke.Key;
            var vk = (uint)KeyInterop.VirtualKeyFromKey(actualKey);

            capturedMod = nativeMod;
            capturedVk = vk;

            display.Text = HotkeyManager.FormatHotkey(nativeMod, vk);

            dialog.Dispatcher.BeginInvoke(() =>
            {
                hkMgr.SetCustomHotkey(capturedMod, capturedVk);
                dialog.Close();
            }, System.Windows.Threading.DispatcherPriority.Background);
        };

        dialog.ShowDialog();
    }
}
