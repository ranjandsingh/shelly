# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Build

```bash
dotnet build Shelly.sln
```

Or open `Shelly.sln` in Visual Studio / Rider and build (Ctrl+Shift+B).

To run:
```bash
dotnet run --project Shelly.csproj
```

There are no tests configured yet.

## Commit Guidelines

Do not add `Co-Authored-By` lines to commit messages.

## Overview

Shelly is a Windows system tray app that provides a floating terminal panel at the top-center of the screen, with automatic IDE project detection. When the user clicks the tray icon or presses Ctrl+`, a floating panel appears with embedded terminal sessions (via xterm.js in WebView2 backed by ConPTY) that auto-`cd` into detected IDE project directories and launch `claude`.

## Architecture

**App lifecycle**: `App.xaml.cs` manages the system tray icon (via Hardcodet.NotifyIcon), the `FloatingPanel`, and the `HotkeyManager`. There is no main window — the app lives in the system tray.

**Terminal embedding**: `ConPtyTerminal` wraps the Windows ConPTY API (CreatePseudoConsole) to spawn shell processes with a pseudoterminal. `TerminalHostControl` hosts a WebView2 control running xterm.js for rendering. Data flows bidirectionally: ConPTY stdout → C# → PostMessage → xterm.js, and xterm.js input → PostMessage → C# → ConPTY stdin.

**Session management**: `SessionStore` (singleton) holds the list of `TerminalSession` objects and the active selection. It coordinates with `IdeDetector` to discover open IDE projects. Sessions use lazy terminal startup.

**Terminal status detection**: `StatusParser` reads terminal output and classifies it into `TerminalStatus` states: `.Working` (spinner chars + token counter), `.WaitingForInput` (user prompt), `.Interrupted`, `.Idle`. The `Idle → TaskCompleted` transition uses a 3-second delay.

**Panel**: `FloatingPanel` is a borderless, topmost WPF Window positioned at top-center of the screen. It activates when shown so the embedded `WebView2` terminal can receive keyboard focus.

**IDE detection**: `IdeDetector` uses `EnumWindows` + window title parsing to detect open VS Code and JetBrains IDE projects.

**Checkpoints**: `CheckpointManager` creates git snapshots using custom refs (`refs/shelly-snapshots/<project>/<timestamp>`) with a temporary `GIT_INDEX_FILE`.

**Global hotkey**: `HotkeyManager` registers Ctrl+` via Win32 `RegisterHotKey`.

## Dependencies

- **Hardcodet.NotifyIcon.Wpf.NetCore** — system tray icon
- **Microsoft.Web.WebView2** — embedded Chromium for xterm.js
- **xterm.js** — terminal emulator (bundled in Resources/)

## Key Directories

- `Models/` — data models (TerminalSession, TerminalStatus)
- `Services/` — business logic (SessionStore, TerminalManager, ConPtyTerminal, IdeDetector, CheckpointManager, StatusParser)
- `Views/` — WPF views (FloatingPanel, SessionTabBar, TerminalHostControl)
- `Interop/` — Win32 P/Invoke declarations and helpers
- `Resources/` — xterm.js files, tray icon, sounds
