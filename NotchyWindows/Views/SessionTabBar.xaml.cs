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
        SessionStore.Instance.AddSession();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        SessionStore.Instance.IsPinned = !SessionStore.Instance.IsPinned;
        if (sender is Button btn)
            btn.Foreground = SessionStore.Instance.IsPinned
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
    }

    private void RenameTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TerminalSession session)
        {
            // Simple rename via input dialog - will improve later
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
}
