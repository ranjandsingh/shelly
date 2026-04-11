using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shelly.Models;
using Shelly.Services;

namespace Shelly.Views;

/// <summary>
/// Handles the inline update check flow in the menu and the install dialog.
/// Extracted from SessionTabBar.
/// </summary>
public static class UpdateFlowController
{
    /// <summary>Run the update flow inline in a menu item: check -> download -> install dialog.</summary>
    public static async void RunInMenu(MenuItem menuItem)
    {
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

        menuItem.IsEnabled = false;
        menuItem.Header = "Checking for updates...";

        var info = await Task.Run(() => UpdateChecker.CheckForUpdateAsync(force: true));

        if (info == null)
        {
            menuItem.Header = $"Up to date (v{currentVersion})";
            await Task.Delay(2000);
            menuItem.Header = "Check for updates";
            menuItem.IsEnabled = true;
            return;
        }

        menuItem.Header = $"Downloading {info.TagName}...";

        var pollStart = DateTime.UtcNow;
        while ((UpdateChecker.DownloadState == UpdateDownloadState.Downloading ||
                UpdateChecker.DownloadState == UpdateDownloadState.None) &&
               DateTime.UtcNow - pollStart < TimeSpan.FromMinutes(5))
        {
            await Task.Delay(300);
        }

        if (UpdateChecker.DownloadState != UpdateDownloadState.Ready)
        {
            menuItem.Header = "Download failed — opening release page...";
            await Task.Delay(1500);
            menuItem.Header = "Check for updates";
            menuItem.IsEnabled = true;
            Process.Start(new ProcessStartInfo { FileName = info.HtmlUrl, UseShellExecute = true });
            return;
        }

        menuItem.Header = $"Install {info.TagName}";
        menuItem.IsEnabled = true;
        ShowInstallDialog(info, currentVersion);
    }

    private static void ShowInstallDialog(UpdateInfo info, string currentVersion)
    {
        var dialog = new Window
        {
            Width = 360, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            BorderThickness = new Thickness(1),
            Topmost = true
        };

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 16) };

        root.Children.Add(new TextBlock
        {
            Text = $"Shelly {info.TagName} is ready to install",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 15, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        root.Children.Add(new TextBlock
        {
            Text = $"You're on v{currentVersion}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 11, Margin = new Thickness(0, 0, 0, 20)
        });

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var laterBtn = new Button
        {
            Content = "Later",
            Width = 70, Height = 30,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0)
        };
        laterBtn.Click += (_, _) => dialog.Close();
        btnPanel.Children.Add(laterBtn);

        var installBtn = new Button
        {
            Content = "Install Now",
            Width = 100, Height = 30,
            Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.SemiBold
        };
        installBtn.Click += async (_, _) =>
        {
            dialog.Close();
            await UpdateChecker.ApplyUpdateAsync(info);
        };
        btnPanel.Children.Add(installBtn);

        root.Children.Add(btnPanel);
        dialog.KeyDown += (_, ke) => { if (ke.Key == Key.Escape) dialog.Close(); };
        dialog.Content = root;
        dialog.ShowDialog();
    }
}
