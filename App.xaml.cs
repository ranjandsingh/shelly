using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Shelly.Services;
using Shelly.Views;

namespace Shelly;

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
        _singleInstanceMutex = new Mutex(true, "Shelly_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Shelly is already running.", "Shelly", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Restore saved sessions if the user opted in
        if (AppSettings.LoadRememberSessions())
            SessionPersistence.Load();

        // IDE detection disabled — title-based detection doesn't reliably resolve full paths
        // IdeDetector.Instance.Detect();

        // Restore saved default shell preference
        var savedShell = AppSettings.LoadDefaultShell();
        if (savedShell != null && System.IO.File.Exists(savedShell))
            ConPtyTerminal.DefaultShell = savedShell;
        else if (savedShell != null)
            AppSettings.SaveDefaultShell(null); // clear stale preference

        // Ensure there's at least one session (e.g., first launch or remember disabled)
        SessionStore.Instance.EnsureDefaultSession();

        _panel = new FloatingPanel();
        // Show immediately in collapsed state (small indicator bar)
        _panel.Show();

        _trayIcon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Resources/windows/icon.ico")),
            ToolTipText = "Shelly"
        };

        _trayIcon.TrayLeftMouseUp += (_, _) => TogglePanel();
        _trayIcon.TrayBalloonTipClicked += async (_, _) =>
        {
            try
            {
                var info = UpdateChecker.LatestUpdate;
                if (info != null)
                    await UpdateChecker.ApplyUpdateAsync(info);
            }
            catch (Exception ex)
            {
                Logger.Log($"Balloon click update failed: {ex.Message}");
            }
        };

        BuildTrayContextMenu();

        // Rebuild the context menu when sessions change so the list stays current
        SessionStore.Instance.Sessions.CollectionChanged += (_, _) => BuildTrayContextMenu();

        _hotkeyManager = new Interop.HotkeyManager();
        _hotkeyManager.HotkeyPressed += () => TogglePanel();
        _hotkeyManager.Register();

        UpdateChecker.UpdateAvailable += () =>
            Dispatcher.Invoke(() =>
            {
                var info = UpdateChecker.LatestUpdate;
                if (info == null || _trayIcon == null) return;
                _trayIcon.ShowBalloonTip(
                    "Update Available",
                    $"Shelly {info.TagName} is available. Click to update.",
                    BalloonIcon.Info);
                BuildTrayContextMenu();
            });

        if (AppSettings.LoadAutoCheckUpdates())
            _ = Task.Run(() => UpdateChecker.CheckForUpdateAsync());
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
                _panel?.ExpandPanel(pinOpen: true);
            };
            contextMenu.Items.Add(item);
        }

        var newSessionItem = new MenuItem { Header = "New Session" };
        newSessionItem.Click += (_, _) =>
        {
            SessionStore.Instance.AddSession();
            _panel?.ExpandPanel(pinOpen: true);
        };
        contextMenu.Items.Add(newSessionItem);

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
                AppSettings.SaveDefaultShell(shellPath);
                BuildTrayContextMenu();
            };
            shellMenu.Items.Add(item);
        }
        contextMenu.Items.Add(shellMenu);

        var rememberItem = new MenuItem
        {
            Header = "Remember Sessions",
            IsCheckable = true,
            IsChecked = AppSettings.LoadRememberSessions()
        };
        rememberItem.Click += (_, _) =>
        {
            var enabled = rememberItem.IsChecked;
            AppSettings.SaveRememberSessions(enabled);
            if (!enabled)
                SessionPersistence.Delete();
        };
        contextMenu.Items.Add(rememberItem);

        contextMenu.Items.Add(new Separator());

        var latestUpdate = UpdateChecker.LatestUpdate;
        if (latestUpdate != null)
        {
            var updateItem = new MenuItem { Header = $"Update to {latestUpdate.TagName}" };
            updateItem.Click += async (_, _) =>
            {
                try { await UpdateChecker.ApplyUpdateAsync(latestUpdate); }
                catch (Exception ex) { Logger.Log($"Menu update failed: {ex.Message}"); }
            };
            contextMenu.Items.Add(updateItem);
        }

        var checkUpdateItem = new MenuItem { Header = "Check for Updates" };
        checkUpdateItem.Click += async (_, _) =>
        {
            try
            {
                var info = await Task.Run(() => UpdateChecker.CheckForUpdateAsync(force: true));
                if (info == null)
                    _trayIcon?.ShowBalloonTip("Up to Date", "You're running the latest version.", BalloonIcon.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"Manual update check failed: {ex.Message}");
            }
        };
        contextMenu.Items.Add(checkUpdateItem);

        var autoCheckItem = new MenuItem
        {
            Header = "Auto-check for Updates",
            IsCheckable = true,
            IsChecked = AppSettings.LoadAutoCheckUpdates()
        };
        autoCheckItem.Click += (_, _) => AppSettings.SaveAutoCheckUpdates(autoCheckItem.IsChecked);
        contextMenu.Items.Add(autoCheckItem);

        contextMenu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "Quit Shelly" };
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
        if (AppSettings.LoadRememberSessions())
            SessionPersistence.Save();

        SleepPrevention.AllowSleep();
        _trayIcon?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
