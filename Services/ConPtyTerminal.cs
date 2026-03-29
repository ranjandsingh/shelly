using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Shelly.Interop;

namespace Shelly.Services;

public class ConPtyTerminal : IDisposable
{
    private IntPtr _ptyHandle;
    private IntPtr _pipeReadHandle;
    private IntPtr _pipeWriteHandle;
    private IntPtr _processHandle;
    private IntPtr _threadHandle;
    private Thread? _readThread;
    internal bool _disposed;
    private short _cols, _rows;

    public event Action<byte[]>? OutputReceived;
    public event Action? ProcessExited;

    public bool Start(string workingDirectory, short cols = 120, short rows = 30)
    {
        _cols = cols;
        _rows = rows;
        Logger.Log($"ConPtyTerminal: Start({workingDirectory}, {cols}x{rows})");

        if (!NativeMethods.CreatePipe(out var inputReadSide, out var inputWriteSide, IntPtr.Zero, 0))
        {
            Logger.Log($"ConPtyTerminal: CreatePipe(input) FAILED, error={Marshal.GetLastWin32Error()}");
            return false;
        }
        if (!NativeMethods.CreatePipe(out var outputReadSide, out var outputWriteSide, IntPtr.Zero, 0))
        {
            Logger.Log($"ConPtyTerminal: CreatePipe(output) FAILED, error={Marshal.GetLastWin32Error()}");
            NativeMethods.CloseHandle(inputReadSide);
            NativeMethods.CloseHandle(inputWriteSide);
            return false;
        }

        var size = new NativeMethods.COORD { X = cols, Y = rows };
        int hr = NativeMethods.CreatePseudoConsole(size, inputReadSide, outputWriteSide, 0, out _ptyHandle);

        // ConPTY duplicates these internally — close our copies regardless of success
        NativeMethods.CloseHandle(inputReadSide);
        NativeMethods.CloseHandle(outputWriteSide);

        if (hr != 0)
        {
            Logger.Log($"ConPtyTerminal: CreatePseudoConsole FAILED, hr=0x{hr:X8}");
            NativeMethods.CloseHandle(outputReadSide);
            NativeMethods.CloseHandle(inputWriteSide);
            return false;
        }

        _pipeReadHandle = outputReadSide;
        _pipeWriteHandle = inputWriteSide;

        var attrListSize = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
        var attrList = Marshal.AllocHGlobal(attrListSize);
        if (!NativeMethods.InitializeProcThreadAttributeList(attrList, 1, 0, ref attrListSize))
        {
            Logger.Log($"ConPtyTerminal: InitializeProcThreadAttributeList FAILED, error={Marshal.GetLastWin32Error()}");
            Marshal.FreeHGlobal(attrList);
            Dispose();
            return false;
        }

        try
        {
            // PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE: pass the HPCON handle directly as lpValue
            // (not a pointer-to-handle — the API stores lpValue as-is).
            if (!NativeMethods.UpdateProcThreadAttribute(
                    attrList, 0,
                    (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    _ptyHandle,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero, IntPtr.Zero))
            {
                Logger.Log($"ConPtyTerminal: UpdateProcThreadAttribute FAILED, error={Marshal.GetLastWin32Error()}");
                Dispose();
                return false;
            }

            var si = new NativeMethods.STARTUPINFOEX();
            si.StartupInfo.cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEX>();
            si.StartupInfo.dwFlags = 0x00000100; // STARTF_USESTDHANDLES — prevents parent console handle inheritance
            si.lpAttributeList = attrList;

            var shell = DefaultShell;
            Logger.Log($"ConPtyTerminal: launching shell: {shell}");

            var result = NativeMethods.CreateProcessW(
                null, shell,
                IntPtr.Zero, IntPtr.Zero,
                false,
                NativeMethods.EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero,
                workingDirectory,
                ref si,
                out var pi);

            if (!result)
            {
                Logger.Log($"ConPtyTerminal: CreateProcessW FAILED, error={Marshal.GetLastWin32Error()}");
                Dispose();
                return false;
            }

            Logger.Log($"ConPtyTerminal: process created, PID={pi.dwProcessId}");

            _processHandle = pi.hProcess;
            _threadHandle = pi.hThread;

            _readThread = new Thread(ReadOutputLoop) { IsBackground = true };
            _readThread.Start();

            return true;
        }
        finally
        {
            NativeMethods.DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
        }
    }

    public void WriteInput(string text)
    {
        if (_disposed || _pipeWriteHandle == IntPtr.Zero) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        NativeMethods.WriteFile(_pipeWriteHandle, bytes, (uint)bytes.Length, out _, IntPtr.Zero);
    }

    public void WriteInput(byte[] data)
    {
        if (_disposed || _pipeWriteHandle == IntPtr.Zero) return;
        NativeMethods.WriteFile(_pipeWriteHandle, data, (uint)data.Length, out _, IntPtr.Zero);
    }

    /// <summary>Configured default shell. Set via tray menu.</summary>
    public static string DefaultShell { get; set; } = DetectBestShell();

    /// <summary>Returns all available shells as (label, path) pairs.</summary>
    public static List<(string Label, string Path)> GetAvailableShells()
    {
        var shells = new List<(string, string)>();

        var bash = WhichOnPath("bash.exe");
        if (bash != null) shells.Add(("Bash", bash));

        var cmd = Environment.GetEnvironmentVariable("COMSPEC") ?? @"C:\Windows\System32\cmd.exe";
        if (File.Exists(cmd)) shells.Add(("Command Prompt (cmd)", cmd));

        var pwsh = WhichOnPath("pwsh.exe");
        if (pwsh != null) shells.Add(("PowerShell 7 (pwsh)", pwsh));

        var winPs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(winPs)) shells.Add(("Windows PowerShell", winPs));

        return shells;
    }

    private static string DetectBestShell()
    {
        // Prefer bash, then cmd, powershell last
        var bash = WhichOnPath("bash.exe");
        if (bash != null) return bash;

        var cmd = Environment.GetEnvironmentVariable("COMSPEC") ?? @"C:\Windows\System32\cmd.exe";
        if (File.Exists(cmd)) return cmd;

        var pwsh = WhichOnPath("pwsh.exe");
        if (pwsh != null) return pwsh;

        var winPs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(winPs)) return winPs;

        return cmd;
    }

    private static string? WhichOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path == null) return null;
        foreach (var dir in path.Split(';'))
        {
            var full = Path.Combine(dir.Trim(), exe);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    public void Resize(short cols, short rows)
    {
        if (_disposed || _ptyHandle == IntPtr.Zero) return;
        _cols = cols;
        _rows = rows;
        var size = new NativeMethods.COORD { X = cols, Y = rows };
        NativeMethods.ResizePseudoConsole(_ptyHandle, size);
    }

    private void ReadOutputLoop()
    {
        Logger.Log("ConPtyTerminal: ReadOutputLoop started");
        var buffer = new byte[4096];
        int readCount = 0;
        while (!_disposed)
        {
            bool success = NativeMethods.ReadFile(_pipeReadHandle, buffer, (uint)buffer.Length, out uint bytesRead, IntPtr.Zero);
            readCount++;
            if (readCount <= 20 || readCount % 50 == 0)
            {
                var preview = success && bytesRead > 0
                    ? Encoding.UTF8.GetString(buffer, 0, (int)Math.Min(bytesRead, 100)).Replace("\r", "\\r").Replace("\n", "\\n").Replace("\x1b", "ESC")
                    : "";
                Logger.Log($"ConPtyTerminal: Read #{readCount}, ok={success}, bytes={bytesRead}: {preview}");
            }

            if (!success || bytesRead == 0)
            {
                Logger.Log($"ConPtyTerminal: Read failed/EOF, error={Marshal.GetLastWin32Error()}");
                ProcessExited?.Invoke();
                break;
            }

            var data = new byte[bytesRead];
            Array.Copy(buffer, data, bytesRead);
            try { OutputReceived?.Invoke(data); }
            catch (Exception ex) { Logger.Log($"ConPtyTerminal: Handler EXCEPTION: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ptyHandle != IntPtr.Zero) { NativeMethods.ClosePseudoConsole(_ptyHandle); _ptyHandle = IntPtr.Zero; }
        if (_processHandle != IntPtr.Zero) { NativeMethods.TerminateProcess(_processHandle, 0); NativeMethods.CloseHandle(_processHandle); _processHandle = IntPtr.Zero; }
        if (_threadHandle != IntPtr.Zero) { NativeMethods.CloseHandle(_threadHandle); _threadHandle = IntPtr.Zero; }
        if (_pipeReadHandle != IntPtr.Zero) { NativeMethods.CloseHandle(_pipeReadHandle); _pipeReadHandle = IntPtr.Zero; }
        if (_pipeWriteHandle != IntPtr.Zero) { NativeMethods.CloseHandle(_pipeWriteHandle); _pipeWriteHandle = IntPtr.Zero; }
    }
}
