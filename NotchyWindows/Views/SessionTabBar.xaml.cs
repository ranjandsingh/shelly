using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotchyWindows.Models;
using NotchyWindows.Services;

namespace NotchyWindows.Views;

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

        menu.Items.Add(new Separator());

        var collapseItem = new MenuItem { Header = "Collapse to bar" };
        collapseItem.Click += (_, _) =>
        {
            if (Window.GetWindow(this) is FloatingPanel panel)
                panel.CollapsePanel();
        };
        menu.Items.Add(collapseItem);

        menu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "Quit Notchy" };
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
}
