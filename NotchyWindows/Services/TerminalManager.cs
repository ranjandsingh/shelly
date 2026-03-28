using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using NotchyWindows.Models;

namespace NotchyWindows.Services;

public class TerminalManager
{
    public static TerminalManager Instance { get; } = new();

    private readonly ConcurrentDictionary<Guid, ConPtyTerminal> _terminals = new();
    private readonly ConcurrentDictionary<Guid, MemoryStream> _outputBuffers = new();
    private readonly ConcurrentDictionary<Guid, Action<byte[]>> _outputHandlers = new();

    public bool HasTerminal(Guid sessionId) => _terminals.ContainsKey(sessionId);

    public void CreateTerminal(Guid sessionId, string workingDirectory, string? projectPath)
    {
        var terminal = new ConPtyTerminal();
        _terminals[sessionId] = terminal;
        _outputBuffers[sessionId] = new MemoryStream();

        terminal.OutputReceived += data =>
        {
            // Buffer the output
            var buffer = _outputBuffers.GetOrAdd(sessionId, _ => new MemoryStream());
            lock (buffer)
            {
                buffer.Write(data, 0, data.Length);
            }

            // Forward to any attached handler (the WebView2 terminal)
            if (_outputHandlers.TryGetValue(sessionId, out var handler))
            {
                Application.Current.Dispatcher.InvokeAsync(() => handler(data));
            }

            // Parse status
            StatusParser.Parse(sessionId, data);
        };

        terminal.ProcessExited += () =>
        {
            var session = SessionStore.Instance.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null)
            {
                Application.Current.Dispatcher.InvokeAsync(() => session.Status = TerminalStatus.Idle);
            }
        };

        if (!terminal.Start(workingDirectory))
            return;

        // Auto-cd and launch claude if CLAUDE.md exists
        if (projectPath != null)
        {
            var claudeMdPath = Path.Combine(projectPath, "CLAUDE.md");
            var hasClaude = File.Exists(claudeMdPath);

            var cdCommand = hasClaude
                ? $"cd \"{projectPath}\" && cls && claude\r\n"
                : $"cd \"{projectPath}\" && cls\r\n";

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
    }

    public void WriteInput(Guid sessionId, string input)
    {
        if (_terminals.TryGetValue(sessionId, out var terminal))
            terminal.WriteInput(input);
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
        _outputHandlers[sessionId] = handler;
    }

    public void RemoveOutputHandler(Guid sessionId)
    {
        _outputHandlers.TryRemove(sessionId, out _);
    }
}
