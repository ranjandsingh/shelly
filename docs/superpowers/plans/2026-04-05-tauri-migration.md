# Shelly Tauri Migration - Implementation Plan

## Context

Shelly is a Windows-only WPF/.NET 8 system tray app with embedded terminal sessions (ConPTY + xterm.js in WebView2). We're doing a clean rewrite to Tauri v2 (Rust + React) to support both Windows and macOS. The full design spec is at `docs/superpowers/specs/2026-04-05-tauri-migration-design.md`.

## Approach

Build the Tauri app in a `shelly-tauri/` subdirectory within the existing repo. Implement in 10 ordered tasks, each producing testable software. Each task builds on the previous. Start with scaffolding, then PTY, then wire everything up.

## Critical Reference Files

- `Services/ConPtyTerminal.cs` — PTY implementation to port
- `Services/TerminalManager.cs` — session→terminal lifecycle  
- `Services/SessionStore.cs` — session state management
- `Services/StatusParser.cs` — terminal output classification
- `Services/AppSettings.cs` — settings persistence
- `Views/FloatingPanel.xaml.cs` — panel expand/collapse/animations
- `Views/TerminalHostControl.xaml.cs` — xterm.js↔C# bridge
- `Views/NotchController.cs` — collapsed notch bar
- `Resources/terminal.html` — xterm.js setup

## Verification

After each task: `cd shelly-tauri && npm run tauri dev`. Final: full feature parity on Windows — tray, hotkey, sessions, terminal I/O, status, sounds, settings persistence.

---

## Task 1: Project Scaffolding

**Goal:** Create the Tauri v2 + React + TypeScript project with all dependencies configured.

**Files to create:**
- `shelly-tauri/` — entire project scaffold
- `shelly-tauri/src-tauri/Cargo.toml` — Rust dependencies
- `shelly-tauri/src-tauri/tauri.conf.json` — window config
- `shelly-tauri/src-tauri/src/lib.rs` — app entry with plugins
- `shelly-tauri/src-tauri/src/main.rs` — main entry
- `shelly-tauri/package.json` — npm dependencies

**Steps:**

- [ ] **Step 1: Scaffold Tauri project**

```bash
cd C:/Users/ranja/Documents/GitHub/shelly
npm create tauri-app@latest -- --template react-ts shelly-tauri
cd shelly-tauri
```

When prompted: identifier `com.shelly.app`, package manager `npm`.

- [ ] **Step 2: Install npm dependencies**

```bash
cd shelly-tauri
npm install xterm @xterm/addon-fit framer-motion
npm install @tauri-apps/plugin-global-shortcut @tauri-apps/plugin-updater
```

- [ ] **Step 3: Add Rust dependencies to Cargo.toml**

Edit `shelly-tauri/src-tauri/Cargo.toml` dependencies section:

```toml
[dependencies]
tauri = { version = "2", features = ["tray-icon", "image-png"] }
tauri-plugin-global-shortcut = "2"
tauri-plugin-single-instance = "2"
tauri-plugin-updater = "2"
portable-pty = "0.9"
serde = { version = "1", features = ["derive"] }
serde_json = "1"
uuid = { version = "1", features = ["v4", "serde"] }
regex = "1"
rodio = "0.20"
log = "0.4"
env_logger = "0.11"
base64 = "0.22"

[target.'cfg(windows)'.dependencies]
windows = { version = "0.58", features = ["Win32_System_Threading", "Win32_System_Power", "Win32_UI_WindowsAndMessaging", "Win32_Foundation"] }
winreg = "0.52"
```

- [ ] **Step 4: Configure tauri.conf.json window**

Replace the windows array in `shelly-tauri/src-tauri/tauri.conf.json`:

```json
{
  "app": {
    "windows": [
      {
        "label": "main",
        "title": "Shelly",
        "width": 720,
        "height": 400,
        "decorations": false,
        "transparent": true,
        "alwaysOnTop": true,
        "resizable": true,
        "visible": false,
        "skipTaskbar": true
      }
    ]
  }
}
```

Also set the identifier to `"com.shelly.app"` and the app name to `"Shelly"`.

- [ ] **Step 5: Set up lib.rs with plugin registration**

Write `shelly-tauri/src-tauri/src/lib.rs`:

```rust
use tauri::Manager;

#[tauri::command]
fn greet(name: &str) -> String {
    format!("Hello, {}!", name)
}

pub fn run() {
    env_logger::init();

    tauri::Builder::default()
        .plugin(
            tauri_plugin_global_shortcut::Builder::new().build(),
        )
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            if let Some(w) = app.get_webview_window("main") {
                let _ = w.show();
                let _ = w.set_focus();
            }
        }))
        .invoke_handler(tauri::generate_handler![greet])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
```

- [ ] **Step 6: Verify build**

```bash
cd shelly-tauri
npm run tauri dev
```

Expected: App compiles, blank window appears (or stays hidden since `visible: false`). No errors.

- [ ] **Step 7: Commit**

```bash
git add shelly-tauri/
git commit -m "feat: scaffold Tauri v2 + React + TypeScript project"
```

---

## Task 2: PTY Layer (Rust Backend)

**Goal:** Spawn shells via portable-pty, read/write I/O, expose as Tauri commands.

**Files to create:**
- `shelly-tauri/src-tauri/src/pty.rs`
- `shelly-tauri/src-tauri/src/shell_detect.rs`

**Files to modify:**
- `shelly-tauri/src-tauri/src/lib.rs` — register commands and state

**Steps:**

- [ ] **Step 1: Create shell detection module**

Write `shelly-tauri/src-tauri/src/shell_detect.rs`:

```rust
use std::env;
use std::path::{Path, PathBuf};

#[derive(Clone, Debug, serde::Serialize)]
pub struct ShellInfo {
    pub label: String,
    pub path: String,
}

pub fn detect_default_shell() -> String {
    #[cfg(target_os = "windows")]
    {
        if let Some(git_bash) = find_git_bash() {
            return git_bash;
        }
        env::var("COMSPEC").unwrap_or_else(|_| r"C:\Windows\System32\cmd.exe".into())
    }
    #[cfg(target_os = "macos")]
    {
        env::var("SHELL").unwrap_or_else(|_| "/bin/zsh".into())
    }
}

pub fn get_available_shells() -> Vec<ShellInfo> {
    let mut shells = Vec::new();

    #[cfg(target_os = "windows")]
    {
        if let Some(path) = find_git_bash() {
            shells.push(ShellInfo { label: "Git Bash".into(), path });
        }
        let system32 = env::var("SystemRoot")
            .map(|r| format!(r"{r}\System32"))
            .unwrap_or_else(|_| r"C:\Windows\System32".into());
        let wsl_bash = format!(r"{system32}\bash.exe");
        if Path::new(&wsl_bash).exists() {
            shells.push(ShellInfo { label: "WSL".into(), path: wsl_bash });
        }
        let cmd = env::var("COMSPEC").unwrap_or_else(|_| r"C:\Windows\System32\cmd.exe".into());
        if Path::new(&cmd).exists() {
            shells.push(ShellInfo { label: "Command Prompt (cmd)".into(), path: cmd });
        }
        if let Some(pwsh) = which_on_path("pwsh.exe") {
            shells.push(ShellInfo { label: "PowerShell 7 (pwsh)".into(), path: pwsh });
        }
        let win_ps = format!(r"{system32}\WindowsPowerShell\v1.0\powershell.exe");
        if Path::new(&win_ps).exists() {
            shells.push(ShellInfo { label: "Windows PowerShell".into(), path: win_ps });
        }
    }

    #[cfg(target_os = "macos")]
    {
        if Path::new("/bin/zsh").exists() {
            shells.push(ShellInfo { label: "zsh".into(), path: "/bin/zsh".into() });
        }
        if Path::new("/bin/bash").exists() {
            shells.push(ShellInfo { label: "bash".into(), path: "/bin/bash".into() });
        }
        // Homebrew bash
        if Path::new("/opt/homebrew/bin/bash").exists() {
            shells.push(ShellInfo { label: "bash (Homebrew)".into(), path: "/opt/homebrew/bin/bash".into() });
        }
        // fish
        for fish_path in &["/opt/homebrew/bin/fish", "/usr/local/bin/fish"] {
            if Path::new(fish_path).exists() {
                shells.push(ShellInfo { label: "fish".into(), path: fish_path.to_string() });
                break;
            }
        }
    }

    shells
}

#[cfg(target_os = "windows")]
fn find_git_bash() -> Option<String> {
    let candidates = [
        r"C:\Program Files\Git\bin\bash.exe",
        r"C:\Program Files (x86)\Git\bin\bash.exe",
    ];
    for c in &candidates {
        if Path::new(c).exists() {
            return Some(c.to_string());
        }
    }
    // Check local appdata
    if let Ok(local) = env::var("LOCALAPPDATA") {
        let p = format!(r"{local}\Programs\Git\bin\bash.exe");
        if Path::new(&p).exists() {
            return Some(p);
        }
    }
    // PATH lookup excluding System32 (that's WSL bash)
    which_on_path_excluding("bash.exe", "System32")
}

fn which_on_path(exe: &str) -> Option<String> {
    let path_var = env::var("PATH").ok()?;
    let sep = if cfg!(windows) { ';' } else { ':' };
    for dir in path_var.split(sep) {
        let full = PathBuf::from(dir.trim()).join(exe);
        if full.exists() {
            return Some(full.to_string_lossy().into());
        }
    }
    None
}

#[cfg(target_os = "windows")]
fn which_on_path_excluding(exe: &str, exclude_containing: &str) -> Option<String> {
    let path_var = env::var("PATH").ok()?;
    for dir in path_var.split(';') {
        let trimmed = dir.trim();
        if trimmed.to_lowercase().contains(&exclude_containing.to_lowercase()) {
            continue;
        }
        let full = PathBuf::from(trimmed).join(exe);
        if full.exists() {
            return Some(full.to_string_lossy().into());
        }
    }
    None
}
```

- [ ] **Step 2: Create PTY module**

Write `shelly-tauri/src-tauri/src/pty.rs`:

```rust
use portable_pty::{native_pty_system, CommandBuilder, PtyPair, PtySize, MasterPty};
use std::collections::HashMap;
use std::io::{Read, Write};
use std::sync::{Arc, Mutex};
use tauri::{AppHandle, Emitter};
use uuid::Uuid;
use base64::Engine;
use base64::engine::general_purpose::STANDARD as BASE64;

const MAX_BUFFER_SIZE: usize = 2 * 1024 * 1024; // 2MB

struct PtyInstance {
    master_writer: Box<dyn Write + Send>,
    master: Box<dyn MasterPty + Send>,
    output_buffer: Vec<u8>,
    suppress_live: bool,
}

pub struct PtyManager {
    instances: Mutex<HashMap<Uuid, Arc<Mutex<PtyInstance>>>>,
}

impl PtyManager {
    pub fn new() -> Self {
        Self {
            instances: Mutex::new(HashMap::new()),
        }
    }

    pub fn create(
        &self,
        session_id: Uuid,
        working_dir: &str,
        shell_path: &str,
        cols: u16,
        rows: u16,
        app: AppHandle,
    ) -> Result<(), String> {
        let pty_system = native_pty_system();
        let pair = pty_system
            .openpty(PtySize {
                rows,
                cols,
                pixel_width: 0,
                pixel_height: 0,
            })
            .map_err(|e| format!("Failed to open PTY: {e}"))?;

        let mut cmd = CommandBuilder::new(shell_path);
        cmd.cwd(working_dir);

        let _child = pair.slave.spawn_command(cmd)
            .map_err(|e| format!("Failed to spawn shell: {e}"))?;

        drop(pair.slave);

        let reader = pair.master.try_clone_reader()
            .map_err(|e| format!("Failed to clone reader: {e}"))?;
        let writer = pair.master.take_writer()
            .map_err(|e| format!("Failed to take writer: {e}"))?;

        let instance = Arc::new(Mutex::new(PtyInstance {
            master_writer: writer,
            master: pair.master,
            output_buffer: Vec::new(),
            suppress_live: false,
        }));

        {
            let mut instances = self.instances.lock().unwrap();
            instances.insert(session_id, instance.clone());
        }

        // Spawn reader thread
        let sid = session_id;
        std::thread::spawn(move || {
            Self::read_loop(reader, sid, instance, app);
        });

        Ok(())
    }

    fn read_loop(
        mut reader: Box<dyn Read + Send>,
        session_id: Uuid,
        instance: Arc<Mutex<PtyInstance>>,
        app: AppHandle,
    ) {
        let mut buf = [0u8; 4096];
        loop {
            match reader.read(&mut buf) {
                Ok(0) => {
                    let _ = app.emit("process-exited", session_id.to_string());
                    break;
                }
                Ok(n) => {
                    let data = buf[..n].to_vec();
                    let b64 = BASE64.encode(&data);

                    let mut inst = instance.lock().unwrap();
                    // Append to buffer (cap at MAX_BUFFER_SIZE)
                    inst.output_buffer.extend_from_slice(&data);
                    if inst.output_buffer.len() > MAX_BUFFER_SIZE {
                        let keep_from = inst.output_buffer.len() - MAX_BUFFER_SIZE / 2;
                        inst.output_buffer = inst.output_buffer[keep_from..].to_vec();
                    }

                    if !inst.suppress_live {
                        drop(inst); // release lock before emit
                        let _ = app.emit("terminal-output", serde_json::json!({
                            "sessionId": session_id.to_string(),
                            "data": b64
                        }));
                    }
                }
                Err(_) => {
                    let _ = app.emit("process-exited", session_id.to_string());
                    break;
                }
            }
        }
    }

    pub fn write_input(&self, session_id: Uuid, data: &[u8]) -> Result<(), String> {
        let instances = self.instances.lock().unwrap();
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let mut inst = instance.lock().unwrap();
        inst.master_writer.write_all(data)
            .map_err(|e| format!("Write failed: {e}"))?;
        inst.master_writer.flush()
            .map_err(|e| format!("Flush failed: {e}"))?;
        Ok(())
    }

    pub fn resize(&self, session_id: Uuid, cols: u16, rows: u16) -> Result<(), String> {
        let instances = self.instances.lock().unwrap();
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let inst = instance.lock().unwrap();
        inst.master.resize(PtySize { rows, cols, pixel_width: 0, pixel_height: 0 })
            .map_err(|e| format!("Resize failed: {e}"))?;
        Ok(())
    }

    pub fn get_buffered_output(&self, session_id: Uuid) -> Result<String, String> {
        let instances = self.instances.lock().unwrap();
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let inst = instance.lock().unwrap();
        Ok(BASE64.encode(&inst.output_buffer))
    }

    pub fn suppress_live_output(&self, session_id: Uuid, suppress: bool) -> Result<(), String> {
        let instances = self.instances.lock().unwrap();
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let mut inst = instance.lock().unwrap();
        inst.suppress_live = suppress;
        Ok(())
    }

    pub fn destroy(&self, session_id: Uuid) {
        let mut instances = self.instances.lock().unwrap();
        instances.remove(&session_id);
        // PtyInstance Drop will clean up the master/writer
    }

    pub fn has_terminal(&self, session_id: Uuid) -> bool {
        let instances = self.instances.lock().unwrap();
        instances.contains_key(&session_id)
    }
}
```

- [ ] **Step 3: Wire PTY commands into lib.rs**

Update `shelly-tauri/src-tauri/src/lib.rs`:

```rust
mod pty;
mod shell_detect;

use std::sync::Mutex;
use tauri::{AppHandle, Manager, State};
use uuid::Uuid;

use pty::PtyManager;
use shell_detect::{ShellInfo, detect_default_shell, get_available_shells};

struct AppState {
    pty_manager: PtyManager,
    default_shell: Mutex<String>,
}

#[tauri::command]
fn create_terminal(
    session_id: String,
    working_dir: String,
    cols: u16,
    rows: u16,
    state: State<'_, AppState>,
    app: AppHandle,
) -> Result<(), String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    let shell = state.default_shell.lock().unwrap().clone();
    state.pty_manager.create(id, &working_dir, &shell, cols, rows, app)
}

#[tauri::command]
fn write_input(session_id: String, data: String, state: State<'_, AppState>) -> Result<(), String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    state.pty_manager.write_input(id, data.as_bytes())
}

#[tauri::command]
fn resize_terminal(session_id: String, cols: u16, rows: u16, state: State<'_, AppState>) -> Result<(), String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    state.pty_manager.resize(id, cols, rows)
}

#[tauri::command]
fn get_buffered_output(session_id: String, state: State<'_, AppState>) -> Result<String, String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    state.pty_manager.get_buffered_output(id)
}

#[tauri::command]
fn suppress_live_output(session_id: String, suppress: bool, state: State<'_, AppState>) -> Result<(), String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    state.pty_manager.suppress_live_output(id, suppress)
}

#[tauri::command]
fn destroy_terminal(session_id: String, state: State<'_, AppState>) {
    if let Ok(id) = Uuid::parse_str(&session_id) {
        state.pty_manager.destroy(id);
    }
}

#[tauri::command]
fn has_terminal(session_id: String, state: State<'_, AppState>) -> bool {
    Uuid::parse_str(&session_id)
        .map(|id| state.pty_manager.has_terminal(id))
        .unwrap_or(false)
}

#[tauri::command]
fn get_available_shells_cmd() -> Vec<ShellInfo> {
    get_available_shells()
}

#[tauri::command]
fn get_default_shell(state: State<'_, AppState>) -> String {
    state.default_shell.lock().unwrap().clone()
}

#[tauri::command]
fn set_default_shell(path: String, state: State<'_, AppState>) {
    *state.default_shell.lock().unwrap() = path;
}

pub fn run() {
    env_logger::init();

    let default_shell = detect_default_shell();

    tauri::Builder::default()
        .manage(AppState {
            pty_manager: PtyManager::new(),
            default_shell: Mutex::new(default_shell),
        })
        .plugin(
            tauri_plugin_global_shortcut::Builder::new().build(),
        )
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            if let Some(w) = app.get_webview_window("main") {
                let _ = w.show();
                let _ = w.set_focus();
            }
        }))
        .invoke_handler(tauri::generate_handler![
            create_terminal,
            write_input,
            resize_terminal,
            get_buffered_output,
            suppress_live_output,
            destroy_terminal,
            has_terminal,
            get_available_shells_cmd,
            get_default_shell,
            set_default_shell,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
```

- [ ] **Step 4: Verify build compiles**

```bash
cd shelly-tauri && npm run tauri dev
```

Expected: Compiles without errors. No visible UI yet.

- [ ] **Step 5: Commit**

```bash
git add shelly-tauri/src-tauri/src/
git commit -m "feat: add PTY layer and shell detection (Rust backend)"
```

---

## Task 3: Terminal Rendering (React + xterm.js)

**Goal:** xterm.js running in React, wired to Rust PTY via Tauri IPC. Type a command, see output.

**Files to create:**
- `shelly-tauri/src/components/TerminalView.tsx`
- `shelly-tauri/src/hooks/useTerminal.ts`
- `shelly-tauri/src/lib/ipc.ts`

**Files to modify:**
- `shelly-tauri/src/App.tsx`
- `shelly-tauri/src/styles.css` (or `global.css`)

**Steps:**

- [ ] **Step 1: Create IPC wrapper**

Write `shelly-tauri/src/lib/ipc.ts`:

```typescript
import { invoke } from "@tauri-apps/api/core";
import { listen, UnlistenFn } from "@tauri-apps/api/event";

export async function createTerminal(
  sessionId: string,
  workingDir: string,
  cols: number,
  rows: number
): Promise<void> {
  return invoke("create_terminal", {
    sessionId,
    workingDir,
    cols,
    rows,
  });
}

export async function writeInput(
  sessionId: string,
  data: string
): Promise<void> {
  return invoke("write_input", { sessionId, data });
}

export async function resizeTerminal(
  sessionId: string,
  cols: number,
  rows: number
): Promise<void> {
  return invoke("resize_terminal", { sessionId, cols, rows });
}

export async function getBufferedOutput(
  sessionId: string
): Promise<string> {
  return invoke("get_buffered_output", { sessionId });
}

export async function suppressLiveOutput(
  sessionId: string,
  suppress: boolean
): Promise<void> {
  return invoke("suppress_live_output", { sessionId, suppress });
}

export async function destroyTerminal(sessionId: string): Promise<void> {
  return invoke("destroy_terminal", { sessionId });
}

export async function hasTerminal(sessionId: string): Promise<boolean> {
  return invoke("has_terminal", { sessionId });
}

export interface TerminalOutputEvent {
  sessionId: string;
  data: string; // base64
}

export async function onTerminalOutput(
  callback: (event: TerminalOutputEvent) => void
): Promise<UnlistenFn> {
  return listen<TerminalOutputEvent>("terminal-output", (e) =>
    callback(e.payload)
  );
}

export async function onProcessExited(
  callback: (sessionId: string) => void
): Promise<UnlistenFn> {
  return listen<string>("process-exited", (e) => callback(e.payload));
}
```

- [ ] **Step 2: Create useTerminal hook**

Write `shelly-tauri/src/hooks/useTerminal.ts`:

```typescript
import { useEffect, useRef, useCallback } from "react";
import { Terminal } from "xterm";
import { FitAddon } from "@xterm/addon-fit";
import {
  createTerminal as createPty,
  writeInput,
  resizeTerminal,
  getBufferedOutput,
  suppressLiveOutput,
  hasTerminal as checkHasTerminal,
  onTerminalOutput,
  TerminalOutputEvent,
} from "../lib/ipc";

export function useTerminal(
  containerRef: React.RefObject<HTMLDivElement | null>,
  sessionId: string | null
) {
  const termRef = useRef<Terminal | null>(null);
  const fitAddonRef = useRef<FitAddon | null>(null);
  const unlistenRef = useRef<(() => void) | null>(null);
  const resizeTimerRef = useRef<number | null>(null);
  const currentSessionRef = useRef<string | null>(null);

  // Initialize xterm once
  useEffect(() => {
    if (!containerRef.current || termRef.current) return;

    const term = new Terminal({
      theme: {
        background: "#1a1a1a",
        foreground: "#e6e6e6",
        cursor: "#e6e6e6",
        selectionBackground: "#44475a",
      },
      fontFamily: "Cascadia Code, Menlo, Consolas, monospace",
      fontSize: 11,
      cursorBlink: true,
      allowProposedApi: true,
    });

    const fitAddon = new FitAddon();
    term.loadAddon(fitAddon);
    term.open(containerRef.current);
    fitAddon.fit();

    termRef.current = term;
    fitAddonRef.current = fitAddon;

    // Handle input
    term.onData((data) => {
      if (currentSessionRef.current) {
        writeInput(currentSessionRef.current, data);
      }
    });

    // Handle resize
    const observer = new ResizeObserver(() => {
      if (resizeTimerRef.current) clearTimeout(resizeTimerRef.current);
      resizeTimerRef.current = window.setTimeout(() => {
        fitAddon.fit();
        if (currentSessionRef.current) {
          resizeTerminal(currentSessionRef.current, term.cols, term.rows);
        }
      }, 150);
    });
    observer.observe(containerRef.current);

    // Clipboard handling
    term.attachCustomKeyEventHandler((e) => {
      if (e.ctrlKey && e.key === "v" && e.type === "keydown") {
        navigator.clipboard.readText().then((text) => {
          if (currentSessionRef.current) {
            writeInput(currentSessionRef.current, text);
          }
        });
        return false;
      }
      if (e.ctrlKey && e.key === "c" && e.type === "keydown") {
        if (term.hasSelection()) {
          navigator.clipboard.writeText(term.getSelection());
          term.clearSelection();
          return false;
        }
      }
      return true;
    });

    return () => {
      observer.disconnect();
      term.dispose();
      termRef.current = null;
      fitAddonRef.current = null;
    };
  }, [containerRef]);

  // Attach to session
  const attachSession = useCallback(async (newSessionId: string) => {
    const term = termRef.current;
    const fitAddon = fitAddonRef.current;
    if (!term || !fitAddon) return;

    // Cleanup previous listener
    if (unlistenRef.current) {
      unlistenRef.current();
      unlistenRef.current = null;
    }

    currentSessionRef.current = newSessionId;

    const exists = await checkHasTerminal(newSessionId);

    if (exists) {
      // Existing terminal: replay buffer
      await suppressLiveOutput(newSessionId, true);
      term.reset();
      fitAddon.fit();

      const b64 = await getBufferedOutput(newSessionId);
      if (b64) {
        const bytes = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
        term.write(bytes);
      }

      // Subscribe to live output
      unlistenRef.current = await onTerminalOutput((event: TerminalOutputEvent) => {
        if (event.sessionId === newSessionId) {
          const bytes = Uint8Array.from(atob(event.data), (c) => c.charCodeAt(0));
          term.write(bytes);
        }
      });

      await suppressLiveOutput(newSessionId, false);
    } else {
      // New terminal
      term.reset();
      fitAddon.fit();

      // Subscribe to live output first
      unlistenRef.current = await onTerminalOutput((event: TerminalOutputEvent) => {
        if (event.sessionId === newSessionId) {
          const bytes = Uint8Array.from(atob(event.data), (c) => c.charCodeAt(0));
          term.write(bytes);
        }
      });

      // Create PTY at current xterm size
      await createPty(newSessionId, getDefaultWorkingDir(), term.cols, term.rows);
    }

    term.focus();
  }, []);

  // React to session changes
  useEffect(() => {
    if (sessionId) {
      attachSession(sessionId);
    }
    return () => {
      if (unlistenRef.current) {
        unlistenRef.current();
        unlistenRef.current = null;
      }
    };
  }, [sessionId, attachSession]);

  return { term: termRef, fitAddon: fitAddonRef };
}

function getDefaultWorkingDir(): string {
  // Will be replaced with session's working directory in Task 4
  return "";
}
```

- [ ] **Step 3: Create TerminalView component**

Write `shelly-tauri/src/components/TerminalView.tsx`:

```tsx
import { useRef } from "react";
import { useTerminal } from "../hooks/useTerminal";
import "xterm/css/xterm.css";

interface TerminalViewProps {
  sessionId: string | null;
}

export function TerminalView({ sessionId }: TerminalViewProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  useTerminal(containerRef, sessionId);

  return (
    <div
      ref={containerRef}
      style={{
        width: "100%",
        height: "100%",
        background: "#1a1a1a",
        paddingLeft: 6,
      }}
    />
  );
}
```

- [ ] **Step 4: Wire into App.tsx**

Replace `shelly-tauri/src/App.tsx`:

```tsx
import { useState, useEffect } from "react";
import { TerminalView } from "./components/TerminalView";
import { getCurrentWindow } from "@tauri-apps/api/window";
import "./App.css";

function App() {
  const [sessionId] = useState(() => crypto.randomUUID());

  useEffect(() => {
    // Show the window once React is mounted
    getCurrentWindow().show();
  }, []);

  return (
    <div className="app">
      <TerminalView sessionId={sessionId} />
    </div>
  );
}

export default App;
```

- [ ] **Step 5: Set up App.css**

Replace `shelly-tauri/src/App.css`:

```css
html, body, #root {
  margin: 0;
  padding: 0;
  width: 100%;
  height: 100%;
  overflow: hidden;
  background: #1a1a1a;
}

.app {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
}
```

- [ ] **Step 6: Update Tauri capabilities for clipboard**

Add clipboard permission to `shelly-tauri/src-tauri/capabilities/default.json` — add `"clipboard-manager:allow-read-text"` and `"clipboard-manager:allow-write-text"` to the permissions array.

- [ ] **Step 7: Verify — terminal works**

```bash
cd shelly-tauri && npm run tauri dev
```

Expected: Window appears with a working terminal. You can type commands, see output, resize works.

- [ ] **Step 8: Commit**

```bash
git add shelly-tauri/src/
git commit -m "feat: add terminal rendering with xterm.js + Tauri IPC"
```

---

## Task 4: Session Management

**Goal:** Multiple terminal sessions with add/remove/select. Tab bar UI. Rust holds session state.

**Files to create:**
- `shelly-tauri/src-tauri/src/session_store.rs`
- `shelly-tauri/src/hooks/useSessionStore.ts`
- `shelly-tauri/src/components/SessionTabBar.tsx`

**Files to modify:**
- `shelly-tauri/src-tauri/src/lib.rs` — add session commands
- `shelly-tauri/src/App.tsx` — integrate session store + tab bar
- `shelly-tauri/src/hooks/useTerminal.ts` — use session working directory

**Steps:**

- [ ] **Step 1: Create session store (Rust)**

Write `shelly-tauri/src-tauri/src/session_store.rs`:

```rust
use serde::{Deserialize, Serialize};
use std::sync::Mutex;
use uuid::Uuid;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub enum TerminalStatus {
    Idle,
    Working,
    WaitingForInput,
    TaskCompleted,
    Interrupted,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TerminalSession {
    pub id: String,
    pub project_name: String,
    pub project_path: Option<String>,
    pub working_directory: String,
    pub has_started: bool,
    pub status: TerminalStatus,
    pub is_active: bool,
    pub skip_auto_launch: bool,
}

pub struct SessionStore {
    sessions: Mutex<Vec<TerminalSession>>,
    active_session_id: Mutex<Option<String>>,
}

impl SessionStore {
    pub fn new() -> Self {
        Self {
            sessions: Mutex::new(Vec::new()),
            active_session_id: Mutex::new(None),
        }
    }

    pub fn get_sessions(&self) -> Vec<TerminalSession> {
        self.sessions.lock().unwrap().clone()
    }

    pub fn get_active_session_id(&self) -> Option<String> {
        self.active_session_id.lock().unwrap().clone()
    }

    pub fn add_session(
        &self,
        name: Option<String>,
        project_path: Option<String>,
        working_dir: Option<String>,
    ) -> TerminalSession {
        let home = dirs::home_dir()
            .map(|p| p.to_string_lossy().into())
            .unwrap_or_else(|| String::new());
        let wd = working_dir
            .or_else(|| project_path.clone())
            .unwrap_or(home);

        let session = TerminalSession {
            id: Uuid::new_v4().to_string(),
            project_name: name.unwrap_or_else(|| "Terminal".into()),
            project_path,
            working_directory: wd,
            has_started: false,
            status: TerminalStatus::Idle,
            is_active: false,
            skip_auto_launch: false,
        };

        let mut sessions = self.sessions.lock().unwrap();
        sessions.push(session.clone());

        // Auto-select if first session
        let mut active = self.active_session_id.lock().unwrap();
        if active.is_none() {
            *active = Some(session.id.clone());
            drop(active);
            drop(sessions);
            self.update_is_active(&session.id);
        }

        session
    }

    pub fn select_session(&self, session_id: &str) {
        *self.active_session_id.lock().unwrap() = Some(session_id.to_string());
        self.update_is_active(session_id);
    }

    pub fn remove_session(&self, session_id: &str) -> Option<String> {
        let mut sessions = self.sessions.lock().unwrap();
        sessions.retain(|s| s.id != session_id);

        let mut active = self.active_session_id.lock().unwrap();
        if active.as_deref() == Some(session_id) {
            *active = sessions.first().map(|s| s.id.clone());
            let new_active = active.clone();
            drop(active);
            drop(sessions);
            if let Some(ref id) = new_active {
                self.update_is_active(id);
            }
            return new_active;
        }
        active.clone()
    }

    pub fn rename_session(&self, session_id: &str, name: &str) {
        let mut sessions = self.sessions.lock().unwrap();
        if let Some(s) = sessions.iter_mut().find(|s| s.id == session_id) {
            s.project_name = name.to_string();
        }
    }

    pub fn set_session_started(&self, session_id: &str) {
        let mut sessions = self.sessions.lock().unwrap();
        if let Some(s) = sessions.iter_mut().find(|s| s.id == session_id) {
            s.has_started = true;
        }
    }

    pub fn update_status(&self, session_id: &str, status: TerminalStatus) {
        let mut sessions = self.sessions.lock().unwrap();
        if let Some(s) = sessions.iter_mut().find(|s| s.id == session_id) {
            s.status = status;
        }
    }

    pub fn get_session(&self, session_id: &str) -> Option<TerminalSession> {
        let sessions = self.sessions.lock().unwrap();
        sessions.iter().find(|s| s.id == session_id).cloned()
    }

    pub fn ensure_default_session(&self) {
        let sessions = self.sessions.lock().unwrap();
        if sessions.is_empty() {
            drop(sessions);
            self.add_session(None, None, None);
        }
    }

    fn update_is_active(&self, active_id: &str) {
        let mut sessions = self.sessions.lock().unwrap();
        for s in sessions.iter_mut() {
            s.is_active = s.id == active_id;
        }
    }
}
```

- [ ] **Step 2: Add `dirs` crate to Cargo.toml**

Add to `shelly-tauri/src-tauri/Cargo.toml`:

```toml
dirs = "5"
```

- [ ] **Step 3: Wire session commands into lib.rs**

Add `mod session_store;` and the session commands to lib.rs. Add `SessionStore` to `AppState`. Register new commands:

```rust
// In AppState:
session_store: session_store::SessionStore,

// Commands:
#[tauri::command]
fn get_sessions(state: State<'_, AppState>) -> Vec<session_store::TerminalSession> {
    state.session_store.get_sessions()
}

#[tauri::command]
fn get_active_session_id(state: State<'_, AppState>) -> Option<String> {
    state.session_store.get_active_session_id()
}

#[tauri::command]
fn add_session(
    name: Option<String>,
    project_path: Option<String>,
    working_dir: Option<String>,
    state: State<'_, AppState>,
) -> session_store::TerminalSession {
    state.session_store.add_session(name, project_path, working_dir)
}

#[tauri::command]
fn select_session(session_id: String, state: State<'_, AppState>) {
    state.session_store.select_session(&session_id);
}

#[tauri::command]
fn remove_session(session_id: String, state: State<'_, AppState>) -> Option<String> {
    state.pty_manager.destroy(uuid::Uuid::parse_str(&session_id).unwrap());
    state.session_store.remove_session(&session_id)
}

#[tauri::command]
fn rename_session(session_id: String, name: String, state: State<'_, AppState>) {
    state.session_store.rename_session(&session_id, &name);
}
```

Add all to `generate_handler![]`. Initialize `session_store: session_store::SessionStore::new()` in AppState and call `state.session_store.ensure_default_session()` in setup.

- [ ] **Step 4: Create useSessionStore hook**

Write `shelly-tauri/src/hooks/useSessionStore.ts`:

```typescript
import { useState, useEffect, useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";

export interface TerminalSession {
  id: string;
  projectName: string;
  projectPath: string | null;
  workingDirectory: string;
  hasStarted: boolean;
  status: string;
  isActive: boolean;
  skipAutoLaunch: boolean;
}

export function useSessionStore() {
  const [sessions, setSessions] = useState<TerminalSession[]>([]);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    const s = await invoke<TerminalSession[]>("get_sessions");
    const active = await invoke<string | null>("get_active_session_id");
    setSessions(s);
    setActiveSessionId(active);
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  const addSession = useCallback(async (name?: string, projectPath?: string, workingDir?: string) => {
    const session = await invoke<TerminalSession>("add_session", {
      name: name ?? null,
      projectPath: projectPath ?? null,
      workingDir: workingDir ?? null,
    });
    await refresh();
    return session;
  }, [refresh]);

  const selectSession = useCallback(async (id: string) => {
    await invoke("select_session", { sessionId: id });
    await refresh();
  }, [refresh]);

  const removeSession = useCallback(async (id: string) => {
    await invoke<string | null>("remove_session", { sessionId: id });
    await refresh();
  }, [refresh]);

  const renameSession = useCallback(async (id: string, name: string) => {
    await invoke("rename_session", { sessionId: id, name });
    await refresh();
  }, [refresh]);

  return {
    sessions,
    activeSessionId,
    addSession,
    selectSession,
    removeSession,
    renameSession,
    refresh,
  };
}
```

- [ ] **Step 5: Create SessionTabBar component**

Write `shelly-tauri/src/components/SessionTabBar.tsx`:

```tsx
import { TerminalSession } from "../hooks/useSessionStore";

interface SessionTabBarProps {
  sessions: TerminalSession[];
  activeSessionId: string | null;
  onSelect: (id: string) => void;
  onAdd: () => void;
  onClose: (id: string) => void;
}

export function SessionTabBar({
  sessions,
  activeSessionId,
  onSelect,
  onAdd,
  onClose,
}: SessionTabBarProps) {
  return (
    <div className="session-tab-bar">
      {sessions.map((s) => (
        <div
          key={s.id}
          className={`tab ${s.id === activeSessionId ? "active" : ""}`}
          onClick={() => onSelect(s.id)}
        >
          <span className="tab-name">{s.projectName}</span>
          {sessions.length > 1 && (
            <button
              className="tab-close"
              onClick={(e) => {
                e.stopPropagation();
                onClose(s.id);
              }}
            >
              x
            </button>
          )}
        </div>
      ))}
      <button className="tab-add" onClick={onAdd}>
        +
      </button>
    </div>
  );
}
```

- [ ] **Step 6: Update App.tsx with session management**

Replace `shelly-tauri/src/App.tsx`:

```tsx
import { useEffect } from "react";
import { TerminalView } from "./components/TerminalView";
import { SessionTabBar } from "./components/SessionTabBar";
import { useSessionStore } from "./hooks/useSessionStore";
import { getCurrentWindow } from "@tauri-apps/api/window";
import "./App.css";

function App() {
  const {
    sessions,
    activeSessionId,
    addSession,
    selectSession,
    removeSession,
  } = useSessionStore();

  useEffect(() => {
    getCurrentWindow().show();
  }, []);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const mod = e.ctrlKey || e.metaKey;
      if (mod && e.key === "t") {
        e.preventDefault();
        addSession();
      }
      if (mod && e.key === "w") {
        e.preventDefault();
        if (activeSessionId && sessions.length > 1) {
          removeSession(activeSessionId);
        }
      }
      if (mod && e.key === "Tab") {
        e.preventDefault();
        if (sessions.length < 2) return;
        const idx = sessions.findIndex((s) => s.id === activeSessionId);
        const next = e.shiftKey
          ? (idx - 1 + sessions.length) % sessions.length
          : (idx + 1) % sessions.length;
        selectSession(sessions[next].id);
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [sessions, activeSessionId, addSession, selectSession, removeSession]);

  return (
    <div className="app">
      <SessionTabBar
        sessions={sessions}
        activeSessionId={activeSessionId}
        onSelect={selectSession}
        onAdd={() => addSession()}
        onClose={removeSession}
      />
      <TerminalView sessionId={activeSessionId} />
    </div>
  );
}

export default App;
```

- [ ] **Step 7: Update useTerminal to use session working directory**

In `useTerminal.ts`, update `attachSession` to fetch the session's working directory from Rust before creating the PTY:

```typescript
// Replace the getDefaultWorkingDir() call with:
const session = await invoke<any>("get_session", { sessionId: newSessionId });
await createPty(newSessionId, session?.workingDirectory ?? "", term.cols, term.rows);
```

Add a `get_session` command in Rust that calls `session_store.get_session()`.

- [ ] **Step 8: Add tab bar CSS to App.css**

Append to `shelly-tauri/src/App.css`:

```css
.session-tab-bar {
  display: flex;
  background: #252525;
  padding: 4px 8px 0;
  gap: 2px;
  flex-shrink: 0;
  user-select: none;
  -webkit-user-select: none;
}

.tab {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  background: #333;
  border-radius: 6px 6px 0 0;
  color: #999;
  font-size: 12px;
  cursor: pointer;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
}

.tab.active {
  background: #1a1a1a;
  color: #e6e6e6;
}

.tab-close {
  background: none;
  border: none;
  color: #666;
  cursor: pointer;
  font-size: 11px;
  padding: 0 2px;
  line-height: 1;
}

.tab-close:hover {
  color: #e6e6e6;
}

.tab-add {
  background: none;
  border: none;
  color: #666;
  cursor: pointer;
  font-size: 16px;
  padding: 4px 8px;
}

.tab-add:hover {
  color: #e6e6e6;
}
```

- [ ] **Step 9: Verify — multiple sessions work**

```bash
cd shelly-tauri && npm run tauri dev
```

Expected: Tab bar shows. Click + to add tabs. Click tabs to switch. Each tab has its own terminal session with independent I/O. Ctrl+T/Ctrl+W shortcuts work.

- [ ] **Step 10: Commit**

```bash
git add shelly-tauri/
git commit -m "feat: add session management with tab bar"
```

---

## Task 5: Floating Panel — Notch, Expand/Collapse, Animations

**Goal:** Collapsed notch bar at top-center. Expand on hover/click. Spring animations. Drag bar. Resize grip.

**Files to create:**
- `shelly-tauri/src/components/FloatingPanel.tsx`
- `shelly-tauri/src/components/Notch.tsx`
- `shelly-tauri/src/components/DragBar.tsx`

**Files to modify:**
- `shelly-tauri/src/App.tsx` — wrap in FloatingPanel
- `shelly-tauri/src/App.css` — panel styles

**Steps:**

- [ ] **Step 1: Create DragBar component**

Write `shelly-tauri/src/components/DragBar.tsx`:

```tsx
export function DragBar() {
  return (
    <div className="drag-bar" data-tauri-drag-region>
      <div className="drag-handle" data-tauri-drag-region />
    </div>
  );
}
```

- [ ] **Step 2: Create Notch component**

Write `shelly-tauri/src/components/Notch.tsx`:

```tsx
import { TerminalSession } from "../hooks/useSessionStore";

interface NotchProps {
  sessions: TerminalSession[];
  onMouseEnter: () => void;
  onClick: () => void;
}

const STATUS_COLORS: Record<string, string> = {
  Idle: "#666",
  Working: "#4a9eff",
  WaitingForInput: "#f5a623",
  TaskCompleted: "#4caf50",
  Interrupted: "#ef5350",
};

export function Notch({ sessions, onMouseEnter, onClick }: NotchProps) {
  return (
    <div
      className="notch"
      onMouseEnter={onMouseEnter}
      onClick={onClick}
    >
      <div className="notch-dots">
        {sessions.map((s) => (
          <div
            key={s.id}
            className="notch-dot"
            style={{ background: STATUS_COLORS[s.status] || "#666" }}
          />
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Create FloatingPanel component**

Write `shelly-tauri/src/components/FloatingPanel.tsx`:

```tsx
import { useState, useEffect, useRef, useCallback } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { currentMonitor } from "@tauri-apps/api/window";
import { Notch } from "./Notch";
import { DragBar } from "./DragBar";
import { TerminalSession } from "../hooks/useSessionStore";

interface FloatingPanelProps {
  sessions: TerminalSession[];
  children: React.ReactNode;
}

const COLLAPSED_WIDTH = 120;
const COLLAPSED_HEIGHT = 28;
const DEFAULT_WIDTH = 720;
const DEFAULT_HEIGHT = 400;

export function FloatingPanel({ sessions, children }: FloatingPanelProps) {
  const [isExpanded, setIsExpanded] = useState(false);
  const [isPinned, setIsPinned] = useState(false);
  const hoverTimerRef = useRef<number | null>(null);
  const appWindow = getCurrentWindow();

  const positionCenter = useCallback(async (width: number, height: number) => {
    const monitor = await currentMonitor();
    if (!monitor) return;
    const screenWidth = monitor.size.width / monitor.scaleFactor;
    const x = (screenWidth - width) / 2;
    await appWindow.setPosition(new (await import("@tauri-apps/api/dpi")).LogicalPosition(x, 0));
    await appWindow.setSize(new (await import("@tauri-apps/api/dpi")).LogicalSize(width, height));
  }, [appWindow]);

  const expand = useCallback(async (pin: boolean = false) => {
    if (isExpanded) return;
    setIsExpanded(true);
    setIsPinned(pin);
    await positionCenter(DEFAULT_WIDTH, DEFAULT_HEIGHT);
    await appWindow.setResizable(true);
  }, [isExpanded, positionCenter, appWindow]);

  const collapse = useCallback(async () => {
    if (!isExpanded) return;
    setIsExpanded(false);
    setIsPinned(false);
    await appWindow.setResizable(false);
    await positionCenter(COLLAPSED_WIDTH, COLLAPSED_HEIGHT);
  }, [isExpanded, positionCenter, appWindow]);

  // Auto-collapse on blur (unless pinned)
  useEffect(() => {
    const handleBlur = () => {
      if (isExpanded && !isPinned) {
        collapse();
      }
    };
    window.addEventListener("blur", handleBlur);
    return () => window.removeEventListener("blur", handleBlur);
  }, [isExpanded, isPinned, collapse]);

  // Hover-to-expand: collapse after 500ms outside
  const handleNotchMouseEnter = useCallback(() => {
    expand(false);
  }, [expand]);

  const handleNotchClick = useCallback(() => {
    expand(true);
  }, [expand]);

  const handlePanelMouseDown = useCallback(() => {
    setIsPinned(true);
  }, []);

  // Position on mount
  useEffect(() => {
    positionCenter(COLLAPSED_WIDTH, COLLAPSED_HEIGHT).then(() => {
      appWindow.show();
    });
  }, []);

  // Handle resize grip
  const handleResizeMouseDown = useCallback(async (e: React.MouseEvent) => {
    e.preventDefault();
    await appWindow.startResizeDragging("SouthEast" as any);
  }, [appWindow]);

  if (!isExpanded) {
    return (
      <Notch
        sessions={sessions}
        onMouseEnter={handleNotchMouseEnter}
        onClick={handleNotchClick}
      />
    );
  }

  return (
    <AnimatePresence>
      <motion.div
        className="floating-panel"
        initial={{ opacity: 0, scale: 0.93, y: -20 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        exit={{ opacity: 0, scale: 0.9, y: -15 }}
        transition={{
          type: "spring",
          stiffness: 300,
          damping: 25,
          mass: 0.8,
        }}
        onMouseDown={handlePanelMouseDown}
      >
        <DragBar />
        {children}
        <div
          className="resize-grip"
          onMouseDown={handleResizeMouseDown}
        />
      </motion.div>
    </AnimatePresence>
  );
}
```

- [ ] **Step 4: Update App.tsx to use FloatingPanel**

Wrap content in FloatingPanel:

```tsx
import { FloatingPanel } from "./components/FloatingPanel";

function App() {
  // ... existing session store hook ...

  return (
    <FloatingPanel sessions={sessions}>
      <div className="app-content">
        <SessionTabBar ... />
        <TerminalView ... />
      </div>
    </FloatingPanel>
  );
}
```

- [ ] **Step 5: Add panel and notch CSS**

Append to `shelly-tauri/src/App.css`:

```css
.notch {
  width: 120px;
  height: 28px;
  background: #252525;
  border-radius: 0 0 10px 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  margin: 0 auto;
}

.notch-dots {
  display: flex;
  gap: 6px;
}

.notch-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.floating-panel {
  width: 100%;
  height: 100%;
  background: #1e1e1e;
  border-radius: 0 0 12px 12px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid #333;
  border-top: none;
}

.app-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.drag-bar {
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  cursor: grab;
}

.drag-handle {
  width: 40px;
  height: 4px;
  background: #444;
  border-radius: 2px;
}

.resize-grip {
  position: absolute;
  bottom: 0;
  right: 0;
  width: 16px;
  height: 16px;
  cursor: nwse-resize;
}
```

- [ ] **Step 6: Verify — panel expands/collapses**

```bash
cd shelly-tauri && npm run tauri dev
```

Expected: Notch pill appears at top-center. Hover expands with spring animation. Click pins it. Clicking outside collapses. Drag bar works. Resize grip works.

- [ ] **Step 7: Commit**

```bash
git add shelly-tauri/src/
git commit -m "feat: add floating panel with notch, animations, drag, resize"
```

---

## Task 6: System Tray & Global Hotkey

**Goal:** Tray icon with context menu. Global Ctrl+`/Cmd+` hotkey toggles panel.

**Files to create:**
- `shelly-tauri/src-tauri/src/tray.rs`

**Files to modify:**
- `shelly-tauri/src-tauri/src/lib.rs` — tray setup + hotkey registration

**Steps:**

- [ ] **Step 1: Create tray module**

Write `shelly-tauri/src-tauri/src/tray.rs`:

```rust
use tauri::{
    menu::{Menu, MenuItem, Submenu, PredefinedMenuItem, CheckMenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    AppHandle, Manager, Emitter,
};

pub fn setup_tray(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let new_session = MenuItem::with_id(app, "new_session", "New Session", true, None::<&str>)?;
    let separator = PredefinedMenuItem::separator(app)?;
    let quit = MenuItem::with_id(app, "quit", "Quit Shelly", true, None::<&str>)?;

    let menu = Menu::with_items(app, &[&new_session, &separator, &quit])?;

    let _tray = TrayIconBuilder::new()
        .icon(app.default_window_icon().unwrap().clone())
        .tooltip("Shelly")
        .menu(&menu)
        .menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id.as_ref() {
            "new_session" => {
                let _ = app.emit("tray-new-session", ());
            }
            "quit" => {
                app.exit(0);
            }
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                let app = tray.app_handle();
                let _ = app.emit("tray-toggle-panel", ());
                if let Some(window) = app.get_webview_window("main") {
                    let _ = window.show();
                    let _ = window.set_focus();
                }
            }
        })
        .build(app)?;

    Ok(())
}
```

- [ ] **Step 2: Set up global hotkey in lib.rs**

Update `lib.rs` setup to register global hotkey and tray:

```rust
mod tray;

// In the Builder, replace the global_shortcut plugin with:
.plugin(
    tauri_plugin_global_shortcut::Builder::new()
        .with_handler(|app, shortcut, event| {
            if event.state == tauri_plugin_global_shortcut::ShortcutState::Pressed {
                let _ = app.emit("tray-toggle-panel", ());
                if let Some(window) = app.get_webview_window("main") {
                    let _ = window.show();
                    let _ = window.set_focus();
                }
            }
        })
        .build(),
)

// In setup closure:
.setup(|app| {
    // ... existing state management ...
    
    tray::setup_tray(app.handle())?;
    
    // Register default hotkey: CmdOrCtrl+`
    use tauri_plugin_global_shortcut::GlobalShortcutExt;
    app.global_shortcut().register("CmdOrCtrl+`")?;
    
    Ok(())
})
```

- [ ] **Step 3: Listen for tray events in React**

In `App.tsx`, add listeners:

```typescript
import { listen } from "@tauri-apps/api/event";

// Inside App component:
useEffect(() => {
  const unlistenToggle = listen("tray-toggle-panel", () => {
    // Toggle panel expanded state — will wire to FloatingPanel
    togglePanel();
  });
  const unlistenNewSession = listen("tray-new-session", () => {
    addSession();
  });
  return () => {
    unlistenToggle.then((f) => f());
    unlistenNewSession.then((f) => f());
  };
}, [addSession]);
```

Lift the expand/collapse toggle from FloatingPanel into App.tsx state so tray events can control it.

- [ ] **Step 4: Copy tray icon files to src-tauri/icons/**

Copy `Resources/icon.png` and the status variant icons to `shelly-tauri/src-tauri/icons/`. Ensure the default icon is set in `tauri.conf.json`.

- [ ] **Step 5: Verify — tray + hotkey**

```bash
cd shelly-tauri && npm run tauri dev
```

Expected: Tray icon appears. Left-click toggles panel. Right-click shows context menu. Ctrl+` (Win) / Cmd+` (Mac) toggles panel. "Quit" exits app.

- [ ] **Step 6: Commit**

```bash
git add shelly-tauri/
git commit -m "feat: add system tray and global hotkey"
```

---

## Task 7: Status Parser

**Goal:** Detect terminal status (Idle/Working/WaitingForInput/TaskCompleted/Interrupted) from PTY output and xterm visible text.

**Files to create:**
- `shelly-tauri/src-tauri/src/status_parser.rs`

**Files to modify:**
- `shelly-tauri/src-tauri/src/lib.rs` — register status commands
- `shelly-tauri/src-tauri/src/pty.rs` — feed output to status parser
- `shelly-tauri/src/hooks/useTerminal.ts` — add visible text polling

**Steps:**

- [ ] **Step 1: Create status parser module**

Write `shelly-tauri/src-tauri/src/status_parser.rs` — port the logic from `Services/StatusParser.cs`:

```rust
use regex::Regex;
use std::collections::HashMap;
use std::sync::Mutex;
use std::time::{Duration, Instant};
use crate::session_store::{SessionStore, TerminalStatus};
use tauri::{AppHandle, Emitter};

pub struct StatusParser {
    completion_pattern: Regex,
    last_working_time: Mutex<HashMap<String, Instant>>,
    completion_timers: Mutex<HashMap<String, Instant>>,
}

impl StatusParser {
    pub fn new() -> Self {
        Self {
            completion_pattern: Regex::new(r"[✢✳✶✻✽].*\bfor\b.*\d+[ms]").unwrap(),
            last_working_time: Mutex::new(HashMap::new()),
            completion_timers: Mutex::new(HashMap::new()),
        }
    }

    /// Fast-path: parse raw PTY output bytes
    pub fn parse_raw_output(
        &self,
        session_id: &str,
        data: &[u8],
        store: &SessionStore,
        app: &AppHandle,
    ) {
        let text = String::from_utf8_lossy(data);
        let session = match store.get_session(session_id) {
            Some(s) => s,
            None => return,
        };

        // Completion message from Working state
        if session.status == TerminalStatus::Working && self.completion_pattern.is_match(&text) {
            log::info!("StatusParser: completion detected in raw output");
            self.set_status(session_id, TerminalStatus::TaskCompleted, store, app);
            return;
        }

        // Start of working from Idle
        if session.status == TerminalStatus::Idle {
            let lower = text.to_lowercase();
            if lower.contains("esc to interrupt")
                || lower.contains("clauding")
                || lower.contains("thinking with")
            {
                self.set_status(session_id, TerminalStatus::Working, store, app);
            }
        }
    }

    /// Parse clean visible text from xterm.js buffer
    pub fn parse_visible_text(
        &self,
        session_id: &str,
        visible_text: &str,
        store: &SessionStore,
        app: &AppHandle,
    ) {
        let session = match store.get_session(session_id) {
            Some(s) => s,
            None => return,
        };

        let new_status = self.classify_visible_text(visible_text, &session.status);
        self.update_status(session_id, new_status, &session.status, store, app);
    }

    fn classify_visible_text(&self, text: &str, current: &TerminalStatus) -> TerminalStatus {
        let lines: Vec<&str> = text
            .split('\n')
            .map(|l| l.trim_end())
            .filter(|l| !l.is_empty())
            .collect();

        if lines.is_empty() {
            return TerminalStatus::Idle;
        }

        let bottom3: Vec<&str> = lines.iter().rev().take(3).copied().collect();
        let bottom3_text = bottom3.join("\n");
        let bottom8: Vec<&str> = lines.iter().rev().take(8).copied().collect();
        let bottom8_text = bottom8.join("\n");

        // Interrupted
        if bottom8_text.to_lowercase().contains("interrupted")
            && !bottom8_text.to_lowercase().contains("esc to interrupt")
        {
            return TerminalStatus::Interrupted;
        }

        // Working (bottom 3 only)
        let b3_lower = bottom3_text.to_lowercase();
        if b3_lower.contains("esc to interrupt")
            || b3_lower.contains("clauding")
            || b3_lower.contains("thinking with")
            || (b3_lower.contains("reading") && b3_lower.contains("file"))
            || (b3_lower.contains("writing") && b3_lower.contains("file"))
        {
            return TerminalStatus::Working;
        }

        // WaitingForInput (bottom 8)
        let b8_lower = bottom8_text.to_lowercase();
        if b8_lower.contains("esc to cancel")
            || b8_lower.contains("do you want to proceed")
            || b8_lower.contains("would you like to proceed")
            || b8_lower.contains("yes / no")
            || b8_lower.contains("(y)es")
            || b8_lower.contains("keep planning")
            || b8_lower.contains("auto-accept edits")
        {
            return TerminalStatus::WaitingForInput;
        }

        // Selector menu
        for line in bottom8.iter() {
            let trimmed = line.trim_start();
            if (trimmed.starts_with('❯') || trimmed.starts_with('?'))
                && trimmed.len() > 1
                && trimmed[1..].trim_start().chars().any(|c| c.is_ascii_digit())
            {
                return TerminalStatus::WaitingForInput;
            }
        }

        // Completion
        let bottom5_text: String = lines.iter().rev().take(5).copied().collect::<Vec<_>>().join("\n");
        if self.completion_pattern.is_match(&bottom5_text) {
            return TerminalStatus::Idle;
        }

        TerminalStatus::Idle
    }

    fn update_status(
        &self,
        session_id: &str,
        new_status: TerminalStatus,
        old_status: &TerminalStatus,
        store: &SessionStore,
        app: &AppHandle,
    ) {
        if new_status == *old_status {
            return;
        }

        // Don't let polling clear TaskCompleted
        if *old_status == TerminalStatus::TaskCompleted && new_status == TerminalStatus::Idle {
            return;
        }

        // Don't go Idle→WaitingForInput
        if *old_status == TerminalStatus::Idle && new_status == TerminalStatus::WaitingForInput {
            return;
        }

        // Sticky Working (2s)
        if *old_status == TerminalStatus::Working && new_status == TerminalStatus::Idle {
            let times = self.last_working_time.lock().unwrap();
            if let Some(last) = times.get(session_id) {
                if last.elapsed() < Duration::from_secs(2) {
                    return;
                }
            }
        }

        self.set_status(session_id, new_status, store, app);
    }

    fn set_status(
        &self,
        session_id: &str,
        status: TerminalStatus,
        store: &SessionStore,
        app: &AppHandle,
    ) {
        if status == TerminalStatus::Working {
            self.last_working_time
                .lock()
                .unwrap()
                .insert(session_id.to_string(), Instant::now());
        }

        store.update_status(session_id, status.clone());
        let _ = app.emit("status-changed", serde_json::json!({
            "sessionId": session_id,
            "status": status,
        }));
    }

    pub fn acknowledge_completion(&self, session_id: &str, store: &SessionStore, app: &AppHandle) {
        if let Some(s) = store.get_session(session_id) {
            if s.status == TerminalStatus::TaskCompleted {
                self.set_status(session_id, TerminalStatus::Idle, store, app);
            }
        }
    }
}
```

- [ ] **Step 2: Wire status parser into PTY read loop**

In `pty.rs`, add `StatusParser` and `SessionStore` references to the read loop so `parse_raw_output` is called on every chunk.

- [ ] **Step 3: Add parse_visible_text command**

In `lib.rs`:

```rust
#[tauri::command]
fn parse_visible_text(session_id: String, text: String, state: State<'_, AppState>, app: AppHandle) {
    state.status_parser.parse_visible_text(&session_id, &text, &state.session_store, &app);
}
```

- [ ] **Step 4: Add 500ms polling in useTerminal.ts**

```typescript
// In useTerminal, after attaching:
const pollInterval = setInterval(async () => {
  if (!termRef.current || !currentSessionRef.current) return;
  const buffer = termRef.current.buffer.active;
  const rows = termRef.current.rows;
  const baseY = buffer.baseY;
  const lines: string[] = [];
  for (let i = baseY; i < baseY + rows; i++) {
    const line = buffer.getLine(i);
    if (line) lines.push(line.translateToString(true));
  }
  const text = lines.join("\n");
  await invoke("parse_visible_text", { sessionId: currentSessionRef.current, text });
}, 500);

// Clear on cleanup
```

- [ ] **Step 5: Listen for status-changed events in useSessionStore**

Update `useSessionStore.ts` to listen for `status-changed` events and update session status in React state.

- [ ] **Step 6: Verify — status detection**

```bash
cd shelly-tauri && npm run tauri dev
```

Run `claude` in the terminal. Observe notch dots changing color: blue during work, amber when waiting for input.

- [ ] **Step 7: Commit**

```bash
git add shelly-tauri/
git commit -m "feat: add terminal status parser with two-path detection"
```

---

## Task 8: Sound Notifications & Sleep Prevention

**Goal:** Play completion sound for background sessions. Prevent OS sleep during work.

**Files to create:**
- `shelly-tauri/src-tauri/src/sound.rs`
- `shelly-tauri/src-tauri/src/sleep_prevention.rs`

**Files to modify:**
- `shelly-tauri/src-tauri/src/lib.rs` — wire up modules
- `shelly-tauri/src-tauri/src/status_parser.rs` — trigger sound + sleep

**Steps:**

- [ ] **Step 1: Create sound module**

Write `shelly-tauri/src-tauri/src/sound.rs`:

```rust
use rodio::{Decoder, OutputStream, Sink};
use std::io::Cursor;

const COMPLETION_SOUND: &[u8] = include_bytes!("../resources/task-complete.wav");

pub fn play_task_completed() {
    std::thread::spawn(|| {
        if let Ok((_stream, stream_handle)) = OutputStream::try_default() {
            if let Ok(source) = Decoder::new(Cursor::new(COMPLETION_SOUND)) {
                let sink = Sink::try_new(&stream_handle).unwrap();
                sink.append(source);
                sink.sleep_until_end();
            }
        }
    });
}
```

Copy `Resources/Sounds/` wav files to `shelly-tauri/src-tauri/resources/`.

- [ ] **Step 2: Create sleep prevention module**

Write `shelly-tauri/src-tauri/src/sleep_prevention.rs`:

```rust
use std::sync::Mutex;

#[cfg(target_os = "windows")]
use windows::Win32::System::Power::SetThreadExecutionState;
#[cfg(target_os = "windows")]
use windows::Win32::System::Power::{ES_CONTINUOUS, ES_SYSTEM_REQUIRED};

#[cfg(target_os = "macos")]
use std::process::{Child, Command};

pub struct SleepPrevention {
    #[cfg(target_os = "macos")]
    caffeinate: Mutex<Option<Child>>,
    active: Mutex<bool>,
}

impl SleepPrevention {
    pub fn new() -> Self {
        Self {
            #[cfg(target_os = "macos")]
            caffeinate: Mutex::new(None),
            active: Mutex::new(false),
        }
    }

    pub fn prevent_sleep(&self) {
        let mut active = self.active.lock().unwrap();
        if *active { return; }
        *active = true;

        #[cfg(target_os = "windows")]
        unsafe {
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
        }

        #[cfg(target_os = "macos")]
        {
            let mut child = self.caffeinate.lock().unwrap();
            if child.is_none() {
                *child = Command::new("caffeinate").arg("-i").spawn().ok();
            }
        }
    }

    pub fn allow_sleep(&self) {
        let mut active = self.active.lock().unwrap();
        if !*active { return; }
        *active = false;

        #[cfg(target_os = "windows")]
        unsafe {
            SetThreadExecutionState(ES_CONTINUOUS);
        }

        #[cfg(target_os = "macos")]
        {
            let mut child = self.caffeinate.lock().unwrap();
            if let Some(mut c) = child.take() {
                let _ = c.kill();
            }
        }
    }
}
```

- [ ] **Step 3: Wire sound into status parser**

When `StatusParser::set_status` transitions to `TaskCompleted`, schedule a 1.5s confirmation timer. If still `TaskCompleted` and the session is not active, call `sound::play_task_completed()`.

- [ ] **Step 4: Wire sleep prevention into session store status updates**

Check if any session has `Working` status → `prevent_sleep()`. If none → `allow_sleep()`.

- [ ] **Step 5: Verify — sound and sleep**

Run `claude` in a background tab. When it completes, sound should play after 1.5s. During work, verify the system doesn't sleep (check via system settings or `powercfg /requests` on Windows).

- [ ] **Step 6: Commit**

```bash
git add shelly-tauri/
git commit -m "feat: add completion sounds and sleep prevention"
```

---

## Task 9: Settings Persistence

**Goal:** Save and load all app settings (shell, font size, hotkey, panel size, etc.) to JSON file.

**Files to create:**
- `shelly-tauri/src-tauri/src/settings.rs`

**Files to modify:**
- `shelly-tauri/src-tauri/src/lib.rs` — settings commands
- `shelly-tauri/src/App.tsx` — load settings on startup

**Steps:**

- [ ] **Step 1: Create settings module**

Write `shelly-tauri/src-tauri/src/settings.rs`:

```rust
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HotkeyConfig {
    pub modifiers: u32,
    pub key: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppSettings {
    #[serde(default = "default_shell")]
    pub default_shell: String,
    #[serde(default)]
    pub remember_sessions: bool,
    #[serde(default = "default_true")]
    pub auto_check_updates: bool,
    #[serde(default)]
    pub auto_launch_claude: bool,
    #[serde(default)]
    pub auto_start: bool,
    #[serde(default = "default_font_size")]
    pub font_size: u16,
    #[serde(default)]
    pub hotkey: Option<HotkeyConfig>,
    #[serde(default)]
    pub notch_at_bottom: bool,
    #[serde(default = "default_panel_width")]
    pub panel_width: f64,
    #[serde(default = "default_panel_height")]
    pub panel_height: f64,
}

fn default_shell() -> String { crate::shell_detect::detect_default_shell() }
fn default_true() -> bool { true }
fn default_font_size() -> u16 { 11 }
fn default_panel_width() -> f64 { 720.0 }
fn default_panel_height() -> f64 { 400.0 }

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            default_shell: default_shell(),
            remember_sessions: false,
            auto_check_updates: true,
            auto_launch_claude: false,
            auto_start: false,
            font_size: default_font_size(),
            hotkey: None,
            notch_at_bottom: false,
            panel_width: default_panel_width(),
            panel_height: default_panel_height(),
        }
    }
}

fn settings_path() -> PathBuf {
    let dir = dirs::config_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("shelly");
    fs::create_dir_all(&dir).ok();
    dir.join("settings.json")
}

pub fn load_settings() -> AppSettings {
    let path = settings_path();
    if let Ok(data) = fs::read_to_string(&path) {
        serde_json::from_str(&data).unwrap_or_default()
    } else {
        AppSettings::default()
    }
}

pub fn save_settings(settings: &AppSettings) {
    let path = settings_path();
    if let Ok(json) = serde_json::to_string_pretty(settings) {
        let _ = fs::write(path, json);
    }
}
```

- [ ] **Step 2: Add settings commands to lib.rs**

```rust
#[tauri::command]
fn get_settings(state: State<'_, AppState>) -> settings::AppSettings {
    state.settings.lock().unwrap().clone()
}

#[tauri::command]
fn save_app_settings(new_settings: settings::AppSettings, state: State<'_, AppState>) {
    settings::save_settings(&new_settings);
    *state.settings.lock().unwrap() = new_settings;
}
```

Add `settings: Mutex<settings::AppSettings>` to `AppState`. Load on startup.

- [ ] **Step 3: Create session persistence**

Add session save/load functions to `session_store.rs`:

```rust
pub fn save_sessions(sessions: &[TerminalSession]) {
    let path = dirs::config_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("shelly")
        .join("sessions.json");
    let json = serde_json::to_string_pretty(sessions).unwrap_or_default();
    let _ = fs::write(path, json);
}

pub fn load_sessions() -> Vec<TerminalSession> {
    let path = dirs::config_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("shelly")
        .join("sessions.json");
    if let Ok(data) = fs::read_to_string(&path) {
        serde_json::from_str(&data).unwrap_or_default()
    } else {
        Vec::new()
    }
}
```

- [ ] **Step 4: Load/save settings on app startup/exit**

In lib.rs `setup`, load settings and apply default shell. On `on_close_requested` or app exit, save sessions if `remember_sessions` is enabled.

- [ ] **Step 5: Verify — settings persist**

Change default shell, restart app. Verify it remembers. Enable remember sessions, create multiple tabs, restart, verify tabs are restored.

- [ ] **Step 6: Commit**

```bash
git add shelly-tauri/
git commit -m "feat: add settings and session persistence"
```

---

## Task 10: Auto-Start, Auto-Update, IDE Detection (Windows)

**Goal:** Final features — auto-start on login, Tauri updater, Windows IDE detection.

**Files to create:**
- `shelly-tauri/src-tauri/src/auto_start.rs`
- `shelly-tauri/src-tauri/src/ide_detector.rs`

**Files to modify:**
- `shelly-tauri/src-tauri/src/lib.rs` — register everything
- `shelly-tauri/src-tauri/tauri.conf.json` — updater config

**Steps:**

- [ ] **Step 1: Create auto-start module**

Write `shelly-tauri/src-tauri/src/auto_start.rs`:

```rust
#[cfg(target_os = "windows")]
pub fn set_auto_start(enabled: bool) -> Result<(), String> {
    use winreg::enums::*;
    use winreg::RegKey;
    use std::env;

    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let run_key = hkcu
        .open_subkey_with_flags(r"Software\Microsoft\Windows\CurrentVersion\Run", KEY_ALL_ACCESS)
        .map_err(|e| e.to_string())?;

    if enabled {
        let exe = env::current_exe().map_err(|e| e.to_string())?;
        run_key.set_value("Shelly", &exe.to_string_lossy().to_string())
            .map_err(|e| e.to_string())?;
    } else {
        let _ = run_key.delete_value("Shelly");
    }
    Ok(())
}

#[cfg(target_os = "macos")]
pub fn set_auto_start(enabled: bool) -> Result<(), String> {
    use std::fs;
    use std::env;

    let plist_path = dirs::home_dir()
        .ok_or("No home dir")?
        .join("Library/LaunchAgents/com.shelly.app.plist");

    if enabled {
        let exe = env::current_exe().map_err(|e| e.to_string())?;
        let plist = format!(
            r#"<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.shelly.app</string>
    <key>ProgramArguments</key>
    <array>
        <string>{}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>"#,
            exe.to_string_lossy()
        );
        fs::write(&plist_path, plist).map_err(|e| e.to_string())?;
    } else {
        let _ = fs::remove_file(&plist_path);
    }
    Ok(())
}
```

- [ ] **Step 2: Create IDE detector (Windows only)**

Write `shelly-tauri/src-tauri/src/ide_detector.rs` — port from `Services/IdeDetector.cs`. Use `windows` crate `EnumWindows`, `GetWindowTextW`, `IsWindowVisible`. Same title-parsing logic for VS Code, Cursor, Windsurf, JetBrains, Zed, Visual Studio, Sublime Text. Wrap the entire module in `#[cfg(target_os = "windows")]`.

- [ ] **Step 3: Configure Tauri updater**

Add to `tauri.conf.json`:

```json
{
  "plugins": {
    "updater": {
      "endpoints": [
        "https://github.com/ranjandsingh/shelly/releases/latest/download/latest.json"
      ],
      "pubkey": ""
    }
  }
}
```

Generate signing keys with `npm run tauri signer generate` and set the pubkey.

- [ ] **Step 4: Add commands to lib.rs**

```rust
#[tauri::command]
fn set_auto_start_cmd(enabled: bool) -> Result<(), String> {
    auto_start::set_auto_start(enabled)
}
```

- [ ] **Step 5: Verify — full feature parity**

Full manual test:
1. App launches, tray icon appears
2. Ctrl+`/Cmd+` toggles panel
3. Notch shows at top-center, hover expands
4. Create/switch/close tabs
5. Terminal I/O works in each session
6. Run `claude`, see status dots change
7. Background completion plays sound
8. Settings persist across restart
9. Shell selection works
10. Drag and resize panel

- [ ] **Step 6: Commit**

```bash
git add shelly-tauri/
git commit -m "feat: add auto-start, updater, and IDE detection"
```

- [ ] **Step 7: Final commit — update CLAUDE.md**

Update the project `CLAUDE.md` to document the new Tauri project structure, build commands (`cd shelly-tauri && npm run tauri dev`), and architecture.

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for Tauri migration"
```

---

## Remaining Features (add during or after tasks above)

These features are in the design spec but should be integrated into the relevant tasks during implementation:

**During Task 3 (Terminal Rendering):**
- Scroll-to-bottom button — same as current terminal.html: appears when user scrolls up, click to jump to bottom

**During Task 4 (Sessions):**
- Auto-cd + Claude launch — after PTY is created, wait for first shell output (or 3s timeout), then send `cd <path> && claude\n` if project has CLAUDE.md and auto_launch_claude is enabled. Shell-specific formatting (bash/zsh vs cmd vs powershell).

**During Task 5 (Floating Panel):**
- Drag-and-drop — listen for Tauri drag-drop events: folder → create session, file → paste path into active terminal
- Bottom-center positioning option — respect `notch_at_bottom` setting

**During Task 6 (Tray + Hotkey):**
- Keybinding dialog — React modal that captures next keydown, calls `invoke("set_custom_hotkey")`, saved to settings
- Tray icon switching — change tray icon based on aggregate session status (any Working → processing icon, any WaitingForInput → waiting icon, any TaskCompleted → success icon, else default)
- Full tray context menu — shell submenu, remember sessions toggle, auto-check updates toggle (match current WPF tray menu)
