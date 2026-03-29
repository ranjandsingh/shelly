using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shelly.Interop;
using Shelly.Models;
using Shelly.Services;

namespace Shelly.Views;

public partial class SessionTabBar : UserControl
{
    public SessionTabBar()
    {
        InitializeComponent();
        DataContext = SessionStore.Instance;
    }

    private void Tab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TerminalSession session)
        {
            SessionStore.Instance.SelectSession(session.Id);
        }
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        var session = SessionStore.Instance.AddSession();
        SessionStore.Instance.SelectSession(session.Id);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select folder for new terminal session"
        };

        if (dialog.ShowDialog() == true)
        {
            var folderPath = dialog.FolderName;
            var folderName = System.IO.Path.GetFileName(folderPath) ?? folderPath;
            var session = SessionStore.Instance.AddSession(folderName, folderPath, folderPath);
            SessionStore.Instance.SelectSession(session.Id);
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        SessionStore.Instance.IsPinned = !SessionStore.Instance.IsPinned;
        if (sender is Button btn)
            btn.Foreground = SessionStore.Instance.IsPinned
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        // Default shell submenu
        var shellMenu = new MenuItem { Header = "Default Shell" };
        foreach (var (label, path) in ConPtyTerminal.GetAvailableShells())
        {
            var shellPath = path;
            var item = new MenuItem
            {
                Header = label,
                IsChecked = string.Equals(ConPtyTerminal.DefaultShell, shellPath, System.StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => ConPtyTerminal.DefaultShell = shellPath;
            shellMenu.Items.Add(item);
        }
        menu.Items.Add(shellMenu);

        var positionItem = new MenuItem
        {
            Header = SessionStore.Instance.NotchAtBottom ? "Move notch to top" : "Move notch to bottom"
        };
        positionItem.Click += (_, _) =>
        {
            SessionStore.Instance.NotchAtBottom = !SessionStore.Instance.NotchAtBottom;
        };
        menu.Items.Add(positionItem);

        // Keybinding option
        var hkMgr = (Application.Current as App)?.HotkeyManager;
        var currentBinding = hkMgr?.CustomVk != null
            ? HotkeyManager.FormatHotkey(hkMgr.CustomModifiers!.Value, hkMgr.CustomVk!.Value)
            : null;
        var keybindItem = new MenuItem
        {
            Header = currentBinding != null ? $"Keybinding: {currentBinding}" : "Set custom keybinding"
        };
        keybindItem.Click += (_, _) => ShowKeybindingDialog();
        menu.Items.Add(keybindItem);

        if (currentBinding != null)
        {
            var clearItem = new MenuItem { Header = "Remove custom keybinding" };
            clearItem.Click += (_, _) => hkMgr?.ClearCustomHotkey();
            menu.Items.Add(clearItem);
        }

        var collapseItem = new MenuItem { Header = "Collapse to bar" };
        collapseItem.Click += (_, _) =>
        {
            if (Window.GetWindow(this) is FloatingPanel panel)
                panel.CollapsePanel();
        };
        menu.Items.Add(collapseItem);

        var autoStartItem = new MenuItem
        {
            Header = "Start with Windows",
            IsChecked = AutoStartManager.IsEnabled
        };
        autoStartItem.Click += (_, _) => AutoStartManager.Toggle();
        menu.Items.Add(autoStartItem);

        var quitItem = new MenuItem { Header = "Quit Shelly" };
        quitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(quitItem);

        menu.PlacementTarget = sender as Button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void RenameTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TerminalSession session)
        {
            var dialog = new Window
            {
                Title = "Rename Session",
                Width = 300, Height = 120,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };
            var panel = new StackPanel { Margin = new Thickness(10) };
            var textBox = new TextBox { Text = session.ProjectName, Margin = new Thickness(0, 0, 0, 10) };
            var okBtn = new Button { Content = "OK", Width = 60, HorizontalAlignment = HorizontalAlignment.Right };
            okBtn.Click += (_, _) => { session.ProjectName = textBox.Text; dialog.Close(); };
            panel.Children.Add(textBox);
            panel.Children.Add(okBtn);
            dialog.Content = panel;
            dialog.ShowDialog();
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TerminalSession session)
        {
            SessionStore.Instance.RemoveSession(session.Id);
        }
    }

    private void CloseTabInline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TerminalSession session)
        {
            if (SessionStore.Instance.Sessions.Count > 1)
                SessionStore.Instance.RemoveSession(session.Id);
        }
    }

    private void ShowKeybindingDialog()
    {
        var hkMgr = (Application.Current as App)?.HotkeyManager;
        if (hkMgr == null) return;

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

        var label = new TextBlock
        {
            Text = "Press Ctrl + key (or Ctrl+Shift, Ctrl+Alt, etc.)",
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
            FontSize = 12, Margin = new Thickness(0, 0, 0, 10),
            TextAlignment = TextAlignment.Center
        };
        panel.Children.Add(label);

        var display = new TextBlock
        {
            Text = "Waiting for keypress...",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(display);

        var hint = new TextBlock
        {
            Text = "Press Escape to cancel",
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(hint);

        dialog.Content = panel;

        dialog.KeyDown += (_, ke) =>
        {
            ke.Handled = true;

            if (ke.Key == Key.Escape) { dialog.Close(); return; }

            // Ignore modifier-only presses
            if (ke.Key == Key.LeftCtrl || ke.Key == Key.RightCtrl ||
                ke.Key == Key.LeftAlt || ke.Key == Key.RightAlt ||
                ke.Key == Key.LeftShift || ke.Key == Key.RightShift ||
                ke.Key == Key.LWin || ke.Key == Key.RWin ||
                ke.Key == Key.System && (ke.SystemKey == Key.LeftAlt || ke.SystemKey == Key.RightAlt))
                return;

            // Must have Ctrl
            var mod = Keyboard.Modifiers;
            if (!mod.HasFlag(ModifierKeys.Control)) return;

            uint nativeMod = NativeMethods.MOD_CONTROL;
            if (mod.HasFlag(ModifierKeys.Alt)) nativeMod |= NativeMethods.MOD_ALT;
            if (mod.HasFlag(ModifierKeys.Shift)) nativeMod |= NativeMethods.MOD_SHIFT;

            // Get the actual key (not the modifier)
            var actualKey = ke.Key == Key.System ? ke.SystemKey : ke.Key;
            var vk = (uint)KeyInterop.VirtualKeyFromKey(actualKey);

            capturedMod = nativeMod;
            capturedVk = vk;

            var combo = HotkeyManager.FormatHotkey(nativeMod, vk);
            display.Text = combo;

            // Auto-apply after a brief moment
            dialog.Dispatcher.BeginInvoke(() =>
            {
                hkMgr.SetCustomHotkey(capturedMod, capturedVk);
                dialog.Close();
            }, System.Windows.Threading.DispatcherPriority.Background);
        };

        dialog.ShowDialog();
    }
}
