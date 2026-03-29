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
    private static readonly string[] Hints =
    [
        "Ctrl+` to toggle panel",
        "Drop a folder to open it",
        "Right-click tab to rename",
        "Pin to keep panel open",
        "Ctrl+T opens a new session",
        "Ctrl+W closes current tab",
        "Drag the top bar to move",
        "Drop a file to paste its path",
        "Customize hotkey in Settings",
        "Notch can go top or bottom",
        "Auto-expands when Claude asks",
    ];

    private int _hintIndex;
    private System.Windows.Threading.DispatcherTimer? _hintTimer;

    private const double ScrollStep = 120;

    public SessionTabBar()
    {
        InitializeComponent();
        DataContext = SessionStore.Instance;

        _hintIndex = Random.Shared.Next(Hints.Length);
        UpdateHintVisibility();

        _hintTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(12)
        };
        _hintTimer.Tick += (_, _) =>
        {
            _hintIndex = (_hintIndex + 1) % Hints.Length;
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (_, _) =>
            {
                HintText.Text = $"Tip: {Hints[_hintIndex]}";
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                HintText.BeginAnimation(OpacityProperty, fadeIn);
            };
            HintText.BeginAnimation(OpacityProperty, fadeOut);
        };
        if (AppSettings.LoadShowHints())
            _hintTimer.Start();

        SizeChanged += (_, _) => UpdateScrollButtons();
    }

    private void UpdateHintVisibility()
    {
        if (!AppSettings.LoadShowHints())
        {
            HintText.Visibility = Visibility.Collapsed;
            return;
        }
        HintText.Text = $"Tip: {Hints[_hintIndex]}";
        HintText.Visibility = Visibility.Visible;
    }

    private void HintText_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Hide hint if squeezed too narrow to be readable
        if (HintText.ActualWidth < 60)
            HintText.Visibility = Visibility.Collapsed;
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
        // Scroll to the end so the new tab is visible
        Dispatcher.BeginInvoke(() => TabScrollViewer.ScrollToRightEnd(),
            System.Windows.Threading.DispatcherPriority.Loaded);
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

            // Re-expand the panel (folder dialog steals focus and triggers collapse)
            if (Window.GetWindow(this) is FloatingPanel panel)
                panel.ExpandPanel(pinOpen: true);
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

        // --- View ---
        var collapseItem = new MenuItem { Header = "Collapse to bar" };
        collapseItem.Click += (_, _) =>
        {
            if (Window.GetWindow(this) is FloatingPanel panel)
                panel.CollapsePanel();
        };
        menu.Items.Add(collapseItem);

        var positionItem = new MenuItem
        {
            Header = SessionStore.Instance.NotchAtBottom ? "Move notch to top" : "Move notch to bottom"
        };
        positionItem.Click += (_, _) =>
        {
            SessionStore.Instance.NotchAtBottom = !SessionStore.Instance.NotchAtBottom;
        };
        menu.Items.Add(positionItem);

        // --- Settings ---
        var settingsMenu = new MenuItem { Header = "Settings" };

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
            item.Click += (_, _) =>
            {
                ConPtyTerminal.DefaultShell = shellPath;
                AppSettings.SaveDefaultShell(shellPath);
            };
            shellMenu.Items.Add(item);
        }
        settingsMenu.Items.Add(shellMenu);

        // Text size submenu
        var textSizeMenu = new MenuItem { Header = "Text Size" };
        var currentFontSize = AppSettings.LoadFontSize();
        var sizes = new[] { ("Small", 9), ("Medium", 11), ("Large", 14), ("Extra Large", 18) };
        foreach (var (label, size) in sizes)
        {
            var s = size;
            var item2 = new MenuItem
            {
                Header = label,
                IsChecked = currentFontSize == s
            };
            item2.Click += (_, _) =>
            {
                AppSettings.SaveFontSize(s);
                if (Window.GetWindow(this) is FloatingPanel panel)
                    panel.TerminalHost.ApplyFontSize(s);
            };
            textSizeMenu.Items.Add(item2);
        }
        settingsMenu.Items.Add(textSizeMenu);

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
        settingsMenu.Items.Add(keybindItem);

        if (currentBinding != null)
        {
            var clearItem = new MenuItem { Header = "Remove custom keybinding" };
            clearItem.Click += (_, _) => hkMgr?.ClearCustomHotkey();
            settingsMenu.Items.Add(clearItem);
        }

        var autoLaunchItem = new MenuItem
        {
            Header = "Auto-launch Claude",
            IsChecked = AppSettings.LoadAutoLaunchClaude()
        };
        autoLaunchItem.Click += (_, _) => AppSettings.SaveAutoLaunchClaude(!AppSettings.LoadAutoLaunchClaude());
        settingsMenu.Items.Add(autoLaunchItem);

        var showHintsItem = new MenuItem
        {
            Header = "Show hints",
            IsChecked = AppSettings.LoadShowHints()
        };
        showHintsItem.Click += (_, _) =>
        {
            var enabled = !AppSettings.LoadShowHints();
            AppSettings.SaveShowHints(enabled);
            if (enabled) { UpdateHintVisibility(); _hintTimer?.Start(); }
            else { HintText.Visibility = Visibility.Collapsed; _hintTimer?.Stop(); }
        };
        settingsMenu.Items.Add(showHintsItem);

        var soundItem = new MenuItem
        {
            Header = "Sound",
            IsChecked = AppSettings.LoadSoundEnabled()
        };
        soundItem.Click += (_, _) => AppSettings.SaveSoundEnabled(!AppSettings.LoadSoundEnabled());
        settingsMenu.Items.Add(soundItem);

        var autoStartItem = new MenuItem
        {
            Header = "Start with Windows",
            IsChecked = AutoStartManager.IsEnabled
        };
        autoStartItem.Click += (_, _) => AutoStartManager.Toggle();
        settingsMenu.Items.Add(autoStartItem);

        menu.Items.Add(settingsMenu);

        // --- App ---
        var updateItem = new MenuItem { Header = "Check for updates" };
        updateItem.Click += async (_, _) =>
        {
            var info = await UpdateChecker.CheckForUpdateAsync(force: true);
            if (info != null)
            {
                var result = System.Windows.MessageBox.Show(
                    $"Shelly {info.TagName} is available.\nYou're on v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}.\n\nInstall now?",
                    "Update Available",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Information);
                if (result == System.Windows.MessageBoxResult.Yes)
                    await UpdateChecker.ApplyUpdateAsync(info);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "You're on the latest version.",
                    "No Updates",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        };
        menu.Items.Add(updateItem);

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

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset - ScrollStep);
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset + ScrollStep);
    }

    private void TabScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void TabScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateScrollButtons();
    }

    private void UpdateScrollButtons()
    {
        if (TabScrollViewer == null) return;

        var canScroll = TabScrollViewer.ScrollableWidth > 0;
        ScrollLeftBtn.Visibility = canScroll && TabScrollViewer.HorizontalOffset > 0
            ? Visibility.Visible : Visibility.Collapsed;
        ScrollRightBtn.Visibility = canScroll && TabScrollViewer.HorizontalOffset < TabScrollViewer.ScrollableWidth
            ? Visibility.Visible : Visibility.Collapsed;
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
