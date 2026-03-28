using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using NotchyWindows.Services;

namespace NotchyWindows.Views;

public partial class TerminalHostControl : UserControl
{
    private Guid? _activeSessionId;
    private bool _webViewReady;

    public TerminalHostControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitializeWebView();
    }

    private async Task InitializeWebView()
    {
        var env = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(Path.GetTempPath(), "NotchyWindows_WebView2"));

        await WebView.EnsureCoreWebView2Async(env);

        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "terminal.html");
        WebView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);

        WebView.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            _webViewReady = true;
            LoadingText.Visibility = Visibility.Collapsed;
        };
    }

    public async void AttachSession(Guid sessionId)
    {
        _activeSessionId = sessionId;

        if (!_webViewReady) return;

        var manager = TerminalManager.Instance;
        if (!manager.HasTerminal(sessionId))
        {
            var store = SessionStore.Instance;
            var session = store.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return;

            manager.CreateTerminal(sessionId, session.WorkingDirectory, session.ProjectPath);
            session.HasStarted = true;
        }

        // Tell xterm.js to clear and prepare for new data
        await WebView.CoreWebView2.ExecuteScriptAsync("terminalReset()");

        // Write any buffered output
        var buffered = manager.GetBufferedOutput(sessionId);
        if (buffered.Length > 0)
        {
            var escaped = Convert.ToBase64String(buffered);
            await WebView.CoreWebView2.ExecuteScriptAsync($"terminalWriteBase64('{escaped}')");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var input = e.TryGetWebMessageAsString();
        if (input != null && _activeSessionId.HasValue)
        {
            TerminalManager.Instance.WriteInput(_activeSessionId.Value, input);
        }
    }

    public async void WriteOutput(byte[] data)
    {
        if (!_webViewReady) return;

        var base64 = Convert.ToBase64String(data);
        await WebView.CoreWebView2.ExecuteScriptAsync($"terminalWriteBase64('{base64}')");
    }
}
