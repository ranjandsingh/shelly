using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using NotchyWindows.Views;

namespace NotchyWindows;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private FloatingPanel? _panel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _panel = new FloatingPanel();

        _trayIcon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Resources/tray-icon.ico")),
            ToolTipText = "Notchy Windows"
        };

        _trayIcon.TrayLeftMouseUp += (_, _) => TogglePanel();

        var contextMenu = new System.Windows.Controls.ContextMenu();

        var newSessionItem = new System.Windows.Controls.MenuItem { Header = "New Session" };
        contextMenu.Items.Add(newSessionItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit Notchy" };
        quitItem.Click += (_, _) =>
        {
            _trayIcon.Dispose();
            _panel.Close();
            Shutdown();
        };
        contextMenu.Items.Add(quitItem);

        _trayIcon.ContextMenu = contextMenu;

        var hotkeyManager = new Interop.HotkeyManager();
        hotkeyManager.HotkeyPressed += () => TogglePanel();
        hotkeyManager.Register();
    }

    private void TogglePanel()
    {
        if (_panel == null) return;

        if (_panel.IsVisible)
            _panel.HidePanel();
        else
            _panel.ShowPanel();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
