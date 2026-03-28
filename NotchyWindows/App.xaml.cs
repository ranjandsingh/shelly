using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using NotchyWindows.Services;
using NotchyWindows.Views;

namespace NotchyWindows;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private TaskbarIcon? _trayIcon;
    private FloatingPanel? _panel;
    private Interop.HotkeyManager? _hotkeyManager;

    public Interop.HotkeyManager? HotkeyManager => _hotkeyManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single-instance check
        _singleInstanceMutex = new Mutex(true, "NotchyWindows_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Notchy Windows is already running.", "Notchy", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Restore saved sessions
        SessionPersistence.Load();

        _panel = new FloatingPanel();
        // Show immediately in collapsed state (small indicator bar)
        _panel.Show();

        _trayIcon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Resources/tray-icon.ico")),
            ToolTipText = "Notchy Windows"
        };

        _trayIcon.TrayLeftMouseUp += (_, _) => TogglePanel();

        BuildTrayContextMenu();

        // Rebuild the context menu when sessions change so the list stays current
        SessionStore.Instance.Sessions.CollectionChanged += (_, _) => BuildTrayContextMenu();

        _hotkeyManager = new Interop.HotkeyManager();
        _hotkeyManager.HotkeyPressed += () => TogglePanel();
        _hotkeyManager.Register();
    }

    private void BuildTrayContextMenu()
    {
        var contextMenu = new ContextMenu();

        // Session list
        foreach (var session in SessionStore.Instance.Sessions)
        {
            var s = session; // capture
            var prefix = s.Id == SessionStore.Instance.ActiveSessionId ? "> " : "  ";
            var item = new MenuItem { Header = $"{prefix}{s.ProjectName}" };
            item.Click += (_, _) =>
            {
                SessionStore.Instance.SelectSession(s.Id);
                _panel?.ExpandPanel();
            };
            contextMenu.Items.Add(item);
        }

        contextMenu.Items.Add(new Separator());

        var newSessionItem = new MenuItem { Header = "New Session" };
        newSessionItem.Click += (_, _) =>
        {
            SessionStore.Instance.AddSession();
            _panel?.ExpandPanel();
        };
        contextMenu.Items.Add(newSessionItem);

        contextMenu.Items.Add(new Separator());

        // Default shell submenu
        var shellMenu = new MenuItem { Header = "Default Shell" };
        foreach (var (label, path) in ConPtyTerminal.GetAvailableShells())
        {
            var shellPath = path;
            var item = new MenuItem
            {
                Header = label,
                IsChecked = string.Equals(ConPtyTerminal.DefaultShell, shellPath, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) =>
            {
                ConPtyTerminal.DefaultShell = shellPath;
                BuildTrayContextMenu();
            };
            shellMenu.Items.Add(item);
        }
        contextMenu.Items.Add(shellMenu);

        contextMenu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "Quit Notchy" };
        quitItem.Click += (_, _) =>
        {
            _trayIcon?.Dispose();
            _panel?.Close();
            Shutdown();
        };
        contextMenu.Items.Add(quitItem);

        if (_trayIcon != null)
            _trayIcon.ContextMenu = contextMenu;
    }

    private void TogglePanel()
    {
        _panel?.TogglePanel();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SessionPersistence.Save();
        SleepPrevention.AllowSleep();
        _trayIcon?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
