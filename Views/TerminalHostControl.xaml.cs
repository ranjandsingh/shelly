using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Shelly.Services;

namespace Shelly.Views;

public partial class TerminalHostControl : UserControl
{
    private Guid? _activeSessionId;
    private Guid? _pendingSessionId;
    private bool _webViewReady;
    private bool _webViewInitStarted;
    private bool _isAttaching;
    private System.Windows.Threading.DispatcherTimer? _statusPollTimer;

    public TerminalHostControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // Poll xterm.js visible buffer every 500ms for Claude status detection
        _statusPollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _statusPollTimer.Tick += async (_, _) => await PollTerminalStatus();

        Logger.Log("TerminalHostControl: constructor");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_webViewInitStarted) return; // Loaded fires again on visibility changes
        _webViewInitStarted = true;
        Logger.Log("TerminalHostControl: OnLoaded fired");
        try
        {
            await InitializeWebView();
        }
        catch (Exception ex)
        {
            Logger.Log($"TerminalHostControl: InitializeWebView EXCEPTION: {ex}");
        }
    }

    private async Task InitializeWebView()
    {
        Logger.Log("TerminalHostControl: InitializeWebView start");

        var userDataFolder = Path.Combine(Path.GetTempPath(), "Shelly_WebView2");
        Logger.Log($"TerminalHostControl: WebView2 userDataFolder={userDataFolder}");

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        Logger.Log("TerminalHostControl: CoreWebView2Environment created");

        await WebView.EnsureCoreWebView2Async(env);
        Logger.Log("TerminalHostControl: EnsureCoreWebView2Async done");

        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        // Auto-grant clipboard permission (prevents "wants to access clipboard" popup)
        WebView.CoreWebView2.PermissionRequested += (_, args) =>
        {
            if (args.PermissionKind == CoreWebView2PermissionKind.ClipboardRead)
                args.State = CoreWebView2PermissionState.Allow;
        };

        // Capture JS console output for debugging
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

        var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "terminal.html");
        Logger.Log($"TerminalHostControl: navigating to {htmlPath}, exists={File.Exists(htmlPath)}");
        WebView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);

        WebView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            Logger.Log($"TerminalHostControl: NavigationCompleted, isSuccess={args.IsSuccess}, httpStatus={args.HttpStatusCode}");
            _webViewReady = true;
            LoadingText.Visibility = Visibility.Collapsed;

            // Attach any session that was queued while WebView2 was loading
            if (_pendingSessionId.HasValue)
            {
                Logger.Log($"TerminalHostControl: attaching pending session {_pendingSessionId.Value}");
                var id = _pendingSessionId.Value;
                _pendingSessionId = null;
                AttachSession(id);
            }
            else
            {
                Logger.Log("TerminalHostControl: no pending session to attach");
            }
        };
    }

    /// <summary>Give keyboard focus to WebView2 and xterm (call after panel show / attach).</summary>
    public void FocusTerminal()
    {
        if (!_webViewReady) return;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Ensure the floating panel is active, then move focus into WebView2's HWND (xterm needs this).
                if (System.Windows.Window.GetWindow(this) is { } window)
                    window.Activate();

                WebView.Focus();
                System.Windows.Input.Keyboard.Focus(WebView);

                _ = WebView.CoreWebView2?.ExecuteScriptAsync("window.focusTerminal && window.focusTerminal()");
            }
            catch (Exception ex)
            {
                Logger.Log($"TerminalHostControl: FocusTerminal: {ex.Message}");
            }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    public async void AttachSession(Guid sessionId)
    {
        Logger.Log($"TerminalHostControl: AttachSession({sessionId}), webViewReady={_webViewReady}");

        _isAttaching = true;
        try
        {
            // Detach previous output handler
            if (_activeSessionId.HasValue)
            {
                Logger.Log($"TerminalHostControl: detaching previous handler for {_activeSessionId.Value}");
                TerminalManager.Instance.RemoveOutputHandler(_activeSessionId.Value);
            }

            _activeSessionId = sessionId;

            // If WebView2 isn't ready yet, queue for later
            if (!_webViewReady)
            {
                Logger.Log($"TerminalHostControl: WebView2 not ready, queuing session {sessionId}");
                _pendingSessionId = sessionId;
                return;
            }

            // Start status polling for this session
            _statusPollTimer?.Start();

            var manager = TerminalManager.Instance;
            var store = SessionStore.Instance;
            var session = store.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null)
            {
                Logger.Log($"TerminalHostControl: session {sessionId} NOT FOUND in store!");
                return;
            }

            bool isNew = !manager.HasTerminal(sessionId);

            if (isNew)
            {
                // New terminal: reset, apply font size, query xterm size, then create terminal
                // at the correct size so no resize is needed (resize during shell startup deadlocks ConPTY).
                Logger.Log("TerminalHostControl: calling terminalReset()");
                await WebView.CoreWebView2.ExecuteScriptAsync("terminalReset()");

                var fontSize = AppSettings.LoadFontSize();
                if (fontSize != 11)
                    await WebView.CoreWebView2.ExecuteScriptAsync($"setFontSize({fontSize})");

                var sizeJson = await WebView.CoreWebView2.ExecuteScriptAsync("JSON.stringify({cols:term.cols,rows:term.rows})");
                short cols = 120, rows = 30;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(sizeJson.Trim('"').Replace("\\\"", "\""));
                    cols = (short)doc.RootElement.GetProperty("cols").GetInt32();
                    rows = (short)doc.RootElement.GetProperty("rows").GetInt32();
                }
                catch (Exception ex)
                {
                    Logger.Log($"TerminalHostControl: size query parse error: {ex.Message}");
                }
                Logger.Log($"TerminalHostControl: xterm size={cols}x{rows}");

                Logger.Log($"TerminalHostControl: setting output handler for session {sessionId}");
                manager.SetOutputHandler(sessionId, WriteOutput);

                Logger.Log($"TerminalHostControl: creating terminal, workDir={session.WorkingDirectory}, projectPath={session.ProjectPath}");
                manager.CreateTerminal(sessionId, session.WorkingDirectory, session.ProjectPath, cols, rows);
                session.HasStarted = true;
            }
            else
            {
                // Existing terminal (tab switch): suppress live output, replay buffer, then resume.
                Logger.Log($"TerminalHostControl: terminal already exists for session {sessionId}");
                manager.BeginSuppressLiveOutput(sessionId);
                try
                {
                    Logger.Log("TerminalHostControl: calling terminalReset()");
                    await WebView.CoreWebView2.ExecuteScriptAsync("terminalReset()");

                    var fontSize = AppSettings.LoadFontSize();
                    if (fontSize != 11)
                        await WebView.CoreWebView2.ExecuteScriptAsync($"setFontSize({fontSize})");

                    var buffered = manager.GetBufferedOutput(sessionId);
                    Logger.Log($"TerminalHostControl: buffered output size={buffered.Length}");
                    if (buffered.Length > 0)
                    {
                        var escaped = Convert.ToBase64String(buffered);
                        Logger.Log($"TerminalHostControl: writing {escaped.Length} chars of base64 buffered data");
                        await WebView.CoreWebView2.ExecuteScriptAsync($"terminalWriteBase64('{escaped}')");
                    }

                    Logger.Log($"TerminalHostControl: setting output handler for session {sessionId}");
                    manager.SetOutputHandler(sessionId, WriteOutput);
                }
                finally
                {
                    manager.EndSuppressLiveOutput(sessionId);
                }
            }

            FocusTerminal();
        }
        finally
        {
            _isAttaching = false;
        }
    }

    public async void ApplyFontSize(int size)
    {
        if (!_webViewReady) return;
        await WebView.CoreWebView2.ExecuteScriptAsync($"setFontSize({size})");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var input = e.TryGetWebMessageAsString();
        if (input == null || !_activeSessionId.HasValue) return;

        // Check if this is a JSON control message (resize)
        if (input.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(input);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "resize")
                {
                    var cols = (short)root.GetProperty("cols").GetInt32();
                    var rows = (short)root.GetProperty("rows").GetInt32();
                    Logger.Log($"TerminalHostControl: resize {cols}x{rows}");
                    TerminalManager.Instance.Resize(_activeSessionId.Value, cols, rows);
                    return;
                }
            }
            catch (JsonException) { /* Not JSON, treat as terminal input */ }
        }

        Logger.Log($"TerminalHostControl: forwarding input ({input.Length} chars) to terminal");
        TerminalManager.Instance.WriteInput(_activeSessionId.Value, input);
    }

    private int _writeOutputCount;

    public void WriteOutput(byte[] data)
    {
        _writeOutputCount++;
        if (_writeOutputCount <= 5 || _writeOutputCount % 50 == 0)
            Logger.Log($"TerminalHostControl: WriteOutput called #{_writeOutputCount}, bytes={data.Length}, webViewReady={_webViewReady}");

        if (!_webViewReady) return;

        var base64 = Convert.ToBase64String(data);
        Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await WebView.CoreWebView2.ExecuteScriptAsync($"terminalWriteBase64('{base64}')");
            }
            catch (Exception ex)
            {
                Logger.Log($"TerminalHostControl: WriteOutput EXCEPTION: {ex.Message}");
            }
        });
    }

    private async Task PollTerminalStatus()
    {
        if (!_webViewReady || !_activeSessionId.HasValue || _isAttaching) return;

        try
        {
            var result = await WebView.CoreWebView2.ExecuteScriptAsync("terminalGetVisibleText()");
            if (result != null && result != "null")
            {
                // ExecuteScriptAsync wraps string results in quotes and escapes
                var text = System.Text.Json.JsonSerializer.Deserialize<string>(result);
                if (text != null)
                    StatusParser.ParseVisibleText(_activeSessionId.Value, text);
            }
        }
        catch { /* ignore polling errors */ }
    }
}
