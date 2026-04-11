# Shelly: Tauri Migration Design

## Summary

Migrate Shelly from a Windows-only WPF/.NET 8 app to a cross-platform Tauri v2 app (Windows + macOS). Clean rewrite with full feature parity. Rust backend replaces all C# services. React frontend replaces WPF XAML + raw HTML. xterm.js terminal rendering is preserved.

## Target Platforms

- Windows (existing)
- macOS (new)

## Tech Stack

| Layer | Current (WPF) | New (Tauri) |
|-------|---------------|-------------|
| Backend | C# / .NET 8 | Rust |
| Frontend framework | WPF XAML | React |
| Terminal rendering | xterm.js in WebView2 | xterm.js in Tauri webview |
| PTY | ConPTY via P/Invoke | `portable-pty` crate (ConPTY on Windows, forkpty on macOS) |
| System tray | Hardcodet.NotifyIcon.Wpf | Tauri built-in tray |
| Global hotkey | Win32 RegisterHotKey | `tauri-plugin-global-shortcut` |
| Auto-update | Custom UpdateChecker + Inno Setup | `tauri-plugin-updater` + GitHub Releases |
| Single instance | Named Mutex | `tauri-plugin-single-instance` |
| Audio | System.Media.SoundPlayer | `rodio` crate |
| Animations | WPF DoubleAnimation + SpringEase | `framer-motion` (React) |

## Project Structure

```
shelly-tauri/
├── src-tauri/                    # Rust backend
│   ├── src/
│   │   ├── main.rs              # App entry, tray, global shortcut
│   │   ├── pty.rs               # Cross-platform PTY (portable-pty)
│   │   ├── terminal_manager.rs  # Session->PTY lifecycle, output buffering
│   │   ├── session_store.rs     # Session state, active selection
│   │   ├── status_parser.rs     # Terminal output -> status classification
│   │   ├── ide_detector.rs      # Window title scanning (Windows only)
│   │   ├── settings.rs          # App settings persistence (JSON file)
│   │   ├── sound.rs             # Notification sounds
│   │   ├── sleep_prevention.rs  # Prevent OS sleep during work
│   │   ├── auto_start.rs        # Launch on login
│   │   └── update_checker.rs    # Tauri updater plugin integration
│   ├── Cargo.toml
│   └── tauri.conf.json
├── src/                          # React frontend
│   ├── App.tsx                   # Root: panel state, hotkey handling
│   ├── components/
│   │   ├── FloatingPanel.tsx     # Expand/collapse, positioning, animations
│   │   ├── Notch.tsx             # Collapsed notch bar with status dots
│   │   ├── SessionTabBar.tsx     # Tab strip with add/close/rename
│   │   ├── TerminalView.tsx      # xterm.js wrapper, IPC bridge to Rust PTY
│   │   └── DragBar.tsx           # Window drag handle (data-tauri-drag-region)
│   ├── hooks/
│   │   ├── useSessionStore.ts    # React state synced with Rust session store
│   │   └── useTerminal.ts        # xterm.js lifecycle + Tauri IPC
│   ├── lib/
│   │   └── ipc.ts               # Typed wrappers around Tauri invoke/listen
│   └── styles/
│       └── global.css
├── package.json
└── index.html
```

## Architecture

### Data Flow

```
Terminal Input:  xterm.js onData -> invoke("write_input") -> Rust -> PTY stdin
Terminal Output: PTY stdout -> Rust -> emit("terminal-output") -> listen() -> xterm.js term.write()
Session State:   Rust SessionStore (source of truth) -> emit("sessions-updated") -> React re-fetches
Status:          Rust parses PTY output inline + React sends visible text every 500ms -> Rust classifies
```

### Key Difference from WPF Version

In the current app, WPF manages the window and WebView2 hosts xterm.js as a child control. In Tauri, the entire UI is the webview -- React handles panel chrome, tabs, and animations, while xterm.js handles the terminal. The Rust backend replaces all C# services.

## Component Design

### 1. PTY Layer (`pty.rs`)

Uses the `portable-pty` crate (from the Wezterm project) which abstracts ConPTY (Windows) and forkpty (macOS).

**Terminal lifecycle:**
1. `create_terminal(session_id, working_dir, cols, rows)` -- spawns shell via `portable-pty::CommandBuilder`
2. Background thread reads PTY output in a loop, emits to frontend and feeds StatusParser + output buffer
3. `write_input(session_id, data)` -- writes to PTY stdin
4. `resize(session_id, cols, rows)` -- resizes PTY
5. `destroy_terminal(session_id)` -- kills process, cleans up

**Shell detection:**
- Windows: Git Bash -> cmd -> pwsh -> PowerShell (same priority as current)
- macOS: zsh (default, `$SHELL`), bash (`/bin/bash` + Homebrew), fish (if installed)
- Both platforms: shell selection in tray menu, saved to settings

**Output buffering:**
- 2MB ring buffer per session (same as current `TerminalManager`)
- `get_buffered_output(session_id)` for replay on tab switch
- `suppress_live_output` / `resume_live_output` during replay

**Auto-cd + Claude launch:**
- Wait for first shell output (or 3s timeout)
- If project path has `CLAUDE.md` and auto-launch is enabled: send `cd <path> && claude\n`
- Shell-specific formatting: bash/zsh use `cd '<path>'`, cmd uses `cd "<path>"`, powershell uses `cd '<path>'; claude`

### 2. Session Management (`session_store.rs`)

**TerminalSession struct:**
```rust
struct TerminalSession {
    id: Uuid,
    project_name: String,
    project_path: Option<String>,
    working_directory: String,
    has_started: bool,
    status: TerminalStatus,  // Idle | Working | WaitingForInput | TaskCompleted | Interrupted
    is_active: bool,
    skip_auto_launch: bool,
}
```

**Tauri commands:**
- `get_sessions()` -- returns all sessions
- `get_active_session()` -- returns active session ID
- `add_session(name?, path?, working_dir?)` -- creates session
- `select_session(id)` -- sets active, emits event
- `remove_session(id)` -- destroys terminal, removes session
- `rename_session(id, name)` -- updates name

**Events emitted to frontend:**
- `sessions-updated` -- session list or property change
- `session-changed` -- active session switched
- `terminal-output` -- `{ session_id, data: base64 }`
- `status-changed` -- `{ session_id, status }`

**Session persistence:**
- Save/load to JSON in app data directory
- Controlled by "Remember Sessions" setting
- Restored sessions have `skip_auto_launch = true`
- Lazy terminal startup on first select

### 3. Frontend -- FloatingPanel, Notch & Animations

**Window configuration (tauri.conf.json):**
- Borderless, transparent, always-on-top
- No taskbar entry (`decorations: false`, `skip_taskbar: true`)
- macOS: requires `transparent: true` and `titleBarStyle: "overlay"` in window config for proper transparent borderless behavior — this is a known Tauri complexity area, test early
- Starts collapsed (notch size)

**Notch (collapsed state):**
- Pill-shaped bar (~120x28px) at top-center (or bottom-center)
- Colored dots per session: idle=gray, working=blue, waiting=amber, completed=green
- Hover -> expand. Click -> expand and pin.

**FloatingPanel (expanded state):**
- Drag bar (uses `data-tauri-drag-region` attribute for native window dragging), tab bar, terminal view
- Custom CSS resize handle at bottom-right (borderless windows don't have native resize grips — use Tauri's `appWindow.startResizing()` API on mousedown)
- Default size: 720x400 (same as current)
- Spring-animated expand: scale 0.93->1.0 + translateY + fadeIn via `framer-motion`
- Collapse: scale 1.0->0.9 + translateY + fadeOut

**Auto-collapse behavior:**
- Hover-opened: collapse after 500ms with mouse outside panel
- Click/hotkey-opened: stays until deactivated
- Window blur: collapse unless pinned (same as current `OnDeactivated`)

**Keyboard shortcuts (Ctrl on Windows, Cmd on macOS — use `CmdOrCtrl` in Tauri):**
- `CmdOrCtrl+Tab` / `CmdOrCtrl+Shift+Tab` -- cycle sessions
- `CmdOrCtrl+T` -- new session
- `CmdOrCtrl+W` -- close session

**Drag & drop:**
- Folder -> create session
- File -> paste path into active terminal

### 4. Terminal Rendering (`TerminalView.tsx`)

**xterm.js setup:**
- Same config: `"Cascadia Code, Menlo, Consolas, monospace"` font (Cascadia Code on Windows, Menlo fallback on macOS), size 11, #1a1a1a bg, cursor blink
- `xterm-addon-fit` for auto-sizing
- One instance, reused across tab switches

**IPC bridge:**
```
Input:  xterm.js onData -> invoke("write_input", { session_id, data })
Output: listen("terminal-output") -> term.write(base64decoded)
```

**Tab switch flow:**
1. Unsubscribe from previous session events
2. `term.reset()`, apply font size
3. `invoke("get_buffered_output")` -> replay into xterm
4. Subscribe to new session events
5. If not started: query xterm cols/rows, `invoke("create_terminal")`
6. Focus terminal

**Resize:** Debounced `ResizeObserver` -> `fitAddon.fit()` -> `invoke("resize")`

**Scroll-to-bottom button:** Same behavior as current.

**Clipboard:** Ctrl+V paste, Ctrl+C copy-or-SIGINT via `attachCustomKeyEventHandler`.

**Status polling:** 500ms timer, reads xterm visible buffer, sends to Rust for classification.

### 5. System Tray & Global Hotkey

**System tray:**
- Tray icon with context menu (same items as current)
- Session list with active indicator
- Shell submenu, Remember Sessions toggle
- Update items, Auto-check toggle
- Quit
- Icon changes per status: default, processing, waiting, success

**Global hotkey:**
- Default: `CmdOrCtrl+`` via `tauri-plugin-global-shortcut` (Ctrl+` on Windows, Cmd+` on macOS)
- Custom hotkey support with keybinding dialog
- Saved to settings, restored on launch

### 6. Status Parser (`status_parser.rs`)

Two-path detection (same as current C# implementation):

**Fast path (`parse_raw_output`):** Called inline on PTY output.
- Completion regex: `[star chars].*\bfor\b.*\d+[ms]`
- Working indicators: "esc to interrupt", "Clauding", "thinking with"
- Only transitions from Idle

**Visible text path (`parse_visible_text`):** Called from frontend every 500ms.
- Bottom 3 lines: Working indicators
- Bottom 8 lines: WaitingForInput prompts ("Esc to cancel", "(Y)es / (N)o", selector menus)
- Interrupted detection

**State machine rules:**
- Idle -> Working: on working indicators
- Working -> TaskCompleted: on completion (immediate) or 3s delay after Working->Idle if worked >10s
- TaskCompleted -> Idle: on user acknowledgement
- 2s sticky on Working (don't drop to Idle too fast)
- No Idle -> WaitingForInput transition

### 7. Sound Notifications (`sound.rs`)

- `rodio` crate for cross-platform audio
- Task completion sound with 1.5s confirmation delay
- Only for background sessions (not active)
- Sound files bundled as Tauri resources

### 8. Sleep Prevention (`sleep_prevention.rs`)

- Windows: `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` via `windows` crate
- macOS: spawn `caffeinate -i` subprocess (simplest, no FFI needed), kill on release
- Active when any session has Working status

### 9. Settings (`settings.rs`)

Stored as JSON in platform app data directory:
- Windows: `%APPDATA%/shelly/settings.json`
- macOS: `~/Library/Application Support/shelly/settings.json`

```rust
struct AppSettings {
    default_shell: String,
    remember_sessions: bool,
    auto_check_updates: bool,
    auto_launch_claude: bool,
    auto_start: bool,
    font_size: u16,
    hotkey: Option<HotkeyConfig>,
    notch_at_bottom: bool,
    panel_width: f64,
    panel_height: f64,
}
```

### 10. Auto-Start (`auto_start.rs`)

- Windows: Registry key `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- macOS: Launch Agent plist `~/Library/LaunchAgents/com.shelly.app.plist`

### 11. Auto-Update

Uses `tauri-plugin-updater` with GitHub Releases endpoint. Tauri handles signature verification, download, and install natively. Replaces custom UpdateChecker + Inno Setup.

- Windows: `.msi` or NSIS `.exe` installer
- macOS: `.dmg` with `.app` bundle

### 12. Single Instance

`tauri-plugin-single-instance` -- duplicate launch focuses existing window.

### 13. IDE Detection (`ide_detector.rs`)

- Windows only (same EnumWindows + title parsing via `windows` crate)
- macOS: excluded for v1 (manual session creation only)
- Detects: VS Code, Cursor, Windsurf, JetBrains IDEs, Zed, Visual Studio, Sublime Text

## Rust Crate Dependencies

| Crate | Purpose |
|-------|---------|
| `tauri` v2 | App framework |
| `tauri-plugin-global-shortcut` | Global hotkey |
| `tauri-plugin-single-instance` | Single instance |
| `tauri-plugin-updater` | Auto-update |
| `portable-pty` | Cross-platform PTY (verify crate name/version at implementation — Wezterm project has restructured crates before) |
| `serde` / `serde_json` | Settings serialization |
| `uuid` | Session IDs |
| `regex` | Status parser patterns |
| `rodio` | Audio playback |
| `windows` (Windows) | Win32 APIs (sleep prevention, IDE detection, registry) |
| `winreg` (Windows) | Auto-start registry |
| `log` + `env_logger` or `tracing` | Logging (replaces current C# Logger service) |

## npm Dependencies

| Package | Purpose |
|---------|---------|
| `react` / `react-dom` | UI framework |
| `@tauri-apps/api` | Tauri IPC (invoke, listen) |
| `@tauri-apps/plugin-*` | Plugin JS bindings |
| `xterm` | Terminal emulator |
| `@xterm/addon-fit` | Terminal auto-sizing |
| `framer-motion` | Spring physics animations |
| `typescript` | Type safety |
| `vite` | Build tool (Tauri default) |

## Exclusions

- IDE detection on macOS (window title parsing not reliable cross-platform; Windows only for now)
- CheckpointManager (git snapshots feature was in original codebase but not actively used)
