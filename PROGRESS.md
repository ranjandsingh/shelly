# Notchy Windows — Progress

## 2026-03-28: Initial Scaffolding (Phases 1–3)

### Project Setup
- Created private GitHub repo: `ranjandsingh/notchy-windows`
- Set up WPF .NET 8 project with solution file
- Installed .NET 8 SDK on machine
- Added NuGet packages: Hardcodet.NotifyIcon.Wpf.NetCore, Microsoft.Web.WebView2
- Created project folder structure: Models/, Services/, Views/, Interop/, Resources/
- Added .gitignore and CLAUDE.md (with no Co-Authored-By rule)

### System Tray (Phase 1)
- Implemented `App.xaml.cs` with system tray icon via Hardcodet.NotifyIcon
- Context menu: New Session, separator, Quit
- Left-click toggles the floating panel
- App runs with no main window (ShutdownMode=OnExplicitShutdown)

### Floating Panel (Phase 1)
- Created `FloatingPanel` — borderless, topmost, non-activating WPF Window
- Positioned at top-center of screen
- Win32 interop: WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW via SetWindowLong
- Draggable top bar, rounded bottom corners, dark theme (#1E1E1E)
- Composed of: drag bar → tab bar → terminal area

### Terminal Backend (Phase 2)
- Implemented `ConPtyTerminal` using direct ConPTY P/Invoke (CreatePseudoConsole)
- No third-party PTY dependency needed — pure Win32 API
- Spawns PowerShell (pwsh or WindowsPowerShell) or falls back to cmd.exe
- Background thread reads PTY output, fires OutputReceived events
- Supports WriteInput (string/byte[]), Resize, and Dispose

### Terminal Frontend (Phase 2)
- Bundled xterm.js 5.3.0 + xterm-addon-fit + xterm.css
- Created `terminal.html` host page with dark theme (bg #1a1a1a, fg #e6e6e6, 11pt monospace)
- Implemented `TerminalHostControl` wrapping WebView2
- Bidirectional data bridge: ConPTY → C# → PostMessage/Base64 → xterm.js (and reverse)
- ResizeObserver sends cols/rows back to C# on terminal resize

### Session & Tab Management (Phase 3)
- `TerminalSession` model with INotifyPropertyChanged (Id, ProjectName, ProjectPath, WorkingDirectory, Status, etc.)
- `TerminalStatus` enum: Idle, Working, WaitingForInput, Interrupted, TaskCompleted
- `SessionStore` singleton: ObservableCollection of sessions, active selection, add/remove/select
- `SessionTabBar` with horizontal tabs, green/gray status dots, rename (context menu), close, "+" button, pin toggle
- Lazy terminal startup — process not spawned until tab first selected

### Services (Phases 4–8 — code written, not yet wired)
- `IdeDetector`: EnumWindows + title parsing for VS Code and JetBrains IDEs, 5-second polling
- `CheckpointManager`: git CLI snapshots using refs/notchy-snapshots/ with temp GIT_INDEX_FILE
- `StatusParser`: classifies terminal output into status states (spinner detection, prompt detection, 3s completion delay)
- `SleepPrevention`: SetThreadExecutionState wrapper
- `HotkeyManager`: Ctrl+` global hotkey via RegisterHotKey

### Win32 Interop
- `NativeMethods.cs`: all P/Invoke declarations (window styles, hotkey, sleep, ConPTY, EnumWindows, cursor)
- `WindowHelper.cs`: MakeNonActivating helper

### Build Status
- **Builds successfully** with `dotnet build` — 0 warnings, 0 errors
