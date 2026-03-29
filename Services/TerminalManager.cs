using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using Shelly.Models;

namespace Shelly.Services;

public class TerminalManager
{
    public static TerminalManager Instance { get; } = new();

    private readonly ConcurrentDictionary<Guid, ConPtyTerminal> _terminals = new();
    private readonly ConcurrentDictionary<Guid, MemoryStream> _outputBuffers = new();
    private readonly ConcurrentDictionary<Guid, Action<byte[]>> _outputHandlers = new();
    /// <summary>When set, PTY output is only buffered (for replay), not forwarded to the WebView yet.</summary>
    private readonly ConcurrentDictionary<Guid, byte> _suppressLiveOutput = new();

    public bool HasTerminal(Guid sessionId) => _terminals.ContainsKey(sessionId);

    public void CreateTerminal(Guid sessionId, string workingDirectory, string? projectPath, short cols = 120, short rows = 30)
    {
        Logger.Log($"TerminalManager: CreateTerminal session={sessionId}, workDir={workingDirectory}, projectPath={projectPath}, size={cols}x{rows}");

        var terminal = new ConPtyTerminal();
        _terminals[sessionId] = terminal;
        _outputBuffers[sessionId] = new MemoryStream();

        int outputCount = 0;
        terminal.OutputReceived += data =>
        {
            outputCount++;
            if (outputCount <= 5 || outputCount % 100 == 0)
                Logger.Log($"TerminalManager: OutputReceived #{outputCount} for session {sessionId}, bytes={data.Length}, hasHandler={_outputHandlers.ContainsKey(sessionId)}");

            // Buffer the output
            var buffer = _outputBuffers.GetOrAdd(sessionId, _ => new MemoryStream());
            lock (buffer)
            {
                buffer.Write(data, 0, data.Length);
            }

            // Forward to any attached handler (the WebView2 terminal), unless replay is in progress
            if (!_suppressLiveOutput.ContainsKey(sessionId) &&
                _outputHandlers.TryGetValue(sessionId, out var handler))
            {
                Application.Current.Dispatcher.InvokeAsync(() => handler(data));
            }

            // Parse status
            StatusParser.Parse(sessionId, data);
        };

        terminal.ProcessExited += () =>
        {
            Logger.Log($"TerminalManager: ProcessExited for session {sessionId}");
            var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null)
            {
                Application.Current.Dispatcher.InvokeAsync(() => session.Status = TerminalStatus.Idle);
            }
        };

        Logger.Log($"TerminalManager: calling terminal.Start({workingDirectory}, {cols}x{rows})");
        if (!terminal.Start(workingDirectory, cols, rows))
        {
            Logger.Log("TerminalManager: terminal.Start FAILED!");
            return;
        }
        Logger.Log("TerminalManager: terminal.Start succeeded");

        // Auto-cd and launch claude if CLAUDE.md exists
        if (projectPath != null)
        {
            var claudeMdPath = Path.Combine(projectPath, "CLAUDE.md");
            var hasClaude = File.Exists(claudeMdPath);
            Logger.Log($"TerminalManager: projectPath={projectPath}, hasClaude={hasClaude}");

            var shellName = Path.GetFileNameWithoutExtension(ConPtyTerminal.DefaultShell).ToLower();
            var cdCommand = shellName switch
            {
                "bash" => hasClaude
                    ? $"cd '{projectPath.Replace("\\", "/")}' && clear && claude\r\n"
                    : $"cd '{projectPath.Replace("\\", "/")}' && clear\r\n",
                "powershell" or "pwsh" => hasClaude
                    ? $"cd '{projectPath}'; clear; claude\r\n"
                    : $"cd '{projectPath}'; clear\r\n",
                _ => hasClaude  // cmd
                    ? $"cd \"{projectPath}\" && cls && claude\r\n"
                    : $"cd \"{projectPath}\" && cls\r\n",
            };

            // Small delay to let shell initialize
            Task.Delay(500).ContinueWith(_ => terminal.WriteInput(cdCommand));
        }
    }

    public void DestroyTerminal(Guid sessionId)
    {
        if (_terminals.TryRemove(sessionId, out var terminal))
            terminal.Dispose();
        _outputBuffers.TryRemove(sessionId, out _);
        _outputHandlers.TryRemove(sessionId, out _);
        _suppressLiveOutput.TryRemove(sessionId, out _);
    }

    public void WriteInput(Guid sessionId, string input)
    {
        if (_terminals.TryGetValue(sessionId, out var terminal))
        {
            terminal.WriteInput(input);
        }
        else
        {
            Logger.Log($"TerminalManager: WriteInput FAILED - no terminal for session {sessionId}");
        }
    }

    public void Resize(Guid sessionId, short cols, short rows)
    {
        if (_terminals.TryGetValue(sessionId, out var terminal))
            terminal.Resize(cols, rows);
    }

    public byte[] GetBufferedOutput(Guid sessionId)
    {
        if (_outputBuffers.TryGetValue(sessionId, out var buffer))
        {
            lock (buffer)
            {
                return buffer.ToArray();
            }
        }
        return Array.Empty<byte>();
    }

    public void SetOutputHandler(Guid sessionId, Action<byte[]> handler)
    {
        Logger.Log($"TerminalManager: SetOutputHandler for session {sessionId}");
        _outputHandlers[sessionId] = handler;
    }

    public void RemoveOutputHandler(Guid sessionId)
    {
        Logger.Log($"TerminalManager: RemoveOutputHandler for session {sessionId}");
        _outputHandlers.TryRemove(sessionId, out _);
    }

    /// <summary>Buffer-only mode while attaching/replaying so no output is lost between replay and handler registration.</summary>
    public void BeginSuppressLiveOutput(Guid sessionId)
    {
        Logger.Log($"TerminalManager: BeginSuppressLiveOutput for session {sessionId}");
        _suppressLiveOutput[sessionId] = 1;
    }

    public void EndSuppressLiveOutput(Guid sessionId)
    {
        Logger.Log($"TerminalManager: EndSuppressLiveOutput for session {sessionId}");
        _suppressLiveOutput.TryRemove(sessionId, out _);
    }
}
