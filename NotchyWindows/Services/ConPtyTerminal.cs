using System.Runtime.InteropServices;
using System.Text;
using NotchyWindows.Interop;

namespace NotchyWindows.Services;

public class ConPtyTerminal : IDisposable
{
    private IntPtr _ptyHandle;
    private IntPtr _pipeReadHandle;
    private IntPtr _pipeWriteHandle;
    private IntPtr _processHandle;
    private IntPtr _threadHandle;
    private Thread? _readThread;
    private bool _disposed;

    public event Action<byte[]>? OutputReceived;
    public event Action? ProcessExited;

    public bool Start(string workingDirectory, short cols = 120, short rows = 30)
    {
        // Create pipes for PTY
        IntPtr inputReadSide, inputWriteSide, outputReadSide, outputWriteSide;

        var sa = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            bInheritHandle = true
        };
        var saPtr = Marshal.AllocHGlobal(Marshal.SizeOf(sa));
        Marshal.StructureToPtr(sa, saPtr, false);

        try
        {
            if (!NativeMethods.CreatePipe(out inputReadSide, out inputWriteSide, saPtr, 0))
                return false;
            if (!NativeMethods.CreatePipe(out outputReadSide, out outputWriteSide, saPtr, 0))
                return false;
        }
        finally
        {
            Marshal.FreeHGlobal(saPtr);
        }

        // Create pseudo console
        var size = new NativeMethods.COORD { X = cols, Y = rows };
        int hr = NativeMethods.CreatePseudoConsole(size, inputReadSide, outputWriteSide, 0, out _ptyHandle);
        if (hr != 0)
            return false;

        // These sides are now owned by the PTY, close our copies
        NativeMethods.CloseHandle(inputReadSide);
        NativeMethods.CloseHandle(outputWriteSide);

        _pipeReadHandle = outputReadSide;
        _pipeWriteHandle = inputWriteSide;

        // Initialize process thread attribute list
        var attrListSize = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
        var attrList = Marshal.AllocHGlobal(attrListSize);
        NativeMethods.InitializeProcThreadAttributeList(attrList, 1, 0, ref attrListSize);

        // Set pseudoconsole attribute
        NativeMethods.UpdateProcThreadAttribute(
            attrList, 0,
            (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            _ptyHandle,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero, IntPtr.Zero);

        // Start the shell process
        var si = new NativeMethods.STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEX>();
        si.lpAttributeList = attrList;

        var shell = Environment.GetEnvironmentVariable("COMSPEC") ?? @"C:\Windows\System32\cmd.exe";
        // Use PowerShell if available
        var pwsh = @"C:\Program Files\PowerShell\7\pwsh.exe";
        var winPwsh = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
        if (System.IO.File.Exists(pwsh))
            shell = pwsh;
        else if (System.IO.File.Exists(winPwsh))
            shell = winPwsh;

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
            return false;

        _processHandle = pi.hProcess;
        _threadHandle = pi.hThread;

        // Start reading output
        _readThread = new Thread(ReadOutputLoop) { IsBackground = true };
        _readThread.Start();

        return true;
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

    public void Resize(short cols, short rows)
    {
        if (_disposed || _ptyHandle == IntPtr.Zero) return;

        var size = new NativeMethods.COORD { X = cols, Y = rows };
        NativeMethods.ResizePseudoConsole(_ptyHandle, size);
    }

    private void ReadOutputLoop()
    {
        var buffer = new byte[4096];
        while (!_disposed)
        {
            bool success = NativeMethods.ReadFile(_pipeReadHandle, buffer, (uint)buffer.Length, out uint bytesRead, IntPtr.Zero);
            if (!success || bytesRead == 0)
            {
                ProcessExited?.Invoke();
                break;
            }

            var data = new byte[bytesRead];
            Array.Copy(buffer, data, bytesRead);
            OutputReceived?.Invoke(data);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ptyHandle != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_ptyHandle);
            _ptyHandle = IntPtr.Zero;
        }
        if (_processHandle != IntPtr.Zero)
        {
            NativeMethods.TerminateProcess(_processHandle, 0);
            NativeMethods.CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }
        if (_threadHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_threadHandle);
            _threadHandle = IntPtr.Zero;
        }
        if (_pipeReadHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_pipeReadHandle);
            _pipeReadHandle = IntPtr.Zero;
        }
        if (_pipeWriteHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_pipeWriteHandle);
            _pipeWriteHandle = IntPtr.Zero;
        }
    }
}
