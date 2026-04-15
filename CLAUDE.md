# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Build

**Frontend + Tauri (dev mode):**
```bash
bun install
bun run tauri dev
```

Vite dev server runs on port 1420 (see `src-tauri/tauri.conf.json`).

**Production build:**
```bash
bun run tauri build
```

**Frontend-only build** (type-check + Vite, no Tauri bundle):
```bash
bun run build
```

There are no tests configured yet.

## Commit Guidelines

Do not add `Co-Authored-By` lines to commit messages.

Do not commit anything under `docs/superpowers/` (specs, plans, and other workflow artifacts). That directory is gitignored and kept local only.

## Overview

Shelly is a cross-platform system tray app built with Tauri v2 that provides a floating terminal panel at the top-center of the screen, with automatic IDE project detection. A small "notch" pill is always visible at the top-center; clicking it (or pressing the configured hotkey, default ``CmdOrCtrl+` ``) animates it into a full floating panel with embedded terminal sessions (xterm.js frontend, portable-pty backend). If a session's working directory contains a `CLAUDE.md` and auto-launch is enabled, the shell runs `claude --continue` after the prompt appears.

## Architecture

Two webview windows are defined in `tauri.conf.json`:
- **`main`** — the floating terminal panel (hidden until triggered, rendered by `src/`).
- **`notch`** — an always-visible small pill at top-center (static `public/notch.html`). Clicking it shows the panel; it hides during panel-visible animation and reappears on hide.

**Frontend** (React 19 + Vite + TypeScript) in `src/`:
- `App.tsx` — root component, wires sessions, theme, hotkey modal, keyboard shortcuts (`Ctrl+T` new tab, `Ctrl+W` close, `Ctrl+Tab`/`Ctrl+Shift+Tab` cycle).
- `components/TerminalView.tsx` — xterm.js rendering, PTY I/O bridge.
- `components/SessionTabBar.tsx` — tab strip + settings entry point.
- `components/SettingsMenu.tsx` — settings UI (themes, fonts, shell, attention sub-menu, auto-start, etc.).
- `components/DragBar.tsx` — draggable top chrome.
- `components/HotkeyCaptureModal.tsx` — captures a new global shortcut.
- `hooks/useSessionStore.ts` — mirrors Rust session store over IPC.
- `hooks/useTerminal.ts` — per-session xterm lifecycle.
- `hooks/useAttention.ts` — listens for `attention-required` events, switches tabs, optionally shows the panel and steals focus, then auto-hides.
- `lib/ipc.ts` — thin wrappers over `invoke()` for terminal commands and event listeners.
- `lib/themes.ts`, `lib/interactionBus.ts`, `lib/terminalFocus.ts` — theming, local interaction events, and focus helpers.

**Backend** (Rust / Tauri v2) in `src-tauri/src/`. `lib.rs` is the entry point: it owns `AppState`, registers all `#[tauri::command]`s, manages the show/hide/pill-grow animation, and wires the blur-to-hide behavior with cooldowns for dialogs, resize, drag, and pin state.

- `pty.rs` — wraps `portable-pty`; spawns shells with a cwd, streams output to the frontend via `terminal-output` events, exposes input/resize/destroy.
- `session_store.rs` — in-memory store of `TerminalSession`s with status and active-selection tracking.
- `status_parser.rs` — classifies terminal output into `TerminalStatus` (`Idle`, `Working`, `WaitingForInput`, `TaskCompleted`, `Interrupted`). Has both raw-PTY fast path and visible-buffer classification. Emits `status-changed` and `attention-required`.
- `settings.rs` — loads/saves `AppSettings` (shell, hotkey, panel size, auto-launch-claude, remember-sessions, auto-start, font size, notch position, `AttentionSettings`) and persisted sessions.
- `tray.rs` — system tray icon/menu.
- `ide_detector.rs` — parses OS window titles to find open VS Code and JetBrains projects.
- `shell_detect.rs` — enumerates available shells (PowerShell, cmd, bash, WSL, etc.) and picks a sensible default per OS.
- `display_info.rs` — detects primary-monitor geometry and `top_inset` (macOS notch / Windows DPI quirks).
- `sleep_prevention.rs` — holds an OS wake-lock while a session is `Working`.
- `sound.rs` — plays notification sounds (via `rodio`) on state transitions.
- `auto_start.rs` — OS-level startup registration.
- `util.rs` — `safe_lock` helper for panic-resistant `Mutex` access.

**IPC**: Frontend ↔ Rust via Tauri's `invoke()` (commands registered at the bottom of `lib.rs`) and `listen()` (events: `terminal-output`, `process-exited`, `status-changed`, `attention-required`, `sessions-updated`, `sessions-force-refresh`, `panel-visibility`, `panel-animating`). Typed wrappers live in `src/lib/ipc.ts`.

## Key Behaviors

- **Panel animation**: on show, the main window starts at the notch's pill size (140×38) and springs into `panel_size`; on hide, it shrinks back. `panel-animating` is emitted so the frontend can round corners during the transition.
- **Blur-to-hide**: the panel auto-hides on lost focus, but is suppressed while `is_pinned`, `animating`, `dialog_open`, or within a 500 ms `hide_cooldown` (set on resize/drag/pin).
- **Auto-launch Claude**: `send_startup_command` sends `claude --continue\r\n` only when `auto_launch_claude` is enabled, the session isn't flagged `skip_auto_launch`, and `CLAUDE.md` exists in the working directory.
- **Attention flow**: when `status_parser` transitions a session into a `trigger_statuses` state (default `TaskCompleted`, `WaitingForInput`), it emits `attention-required`; `useAttention` switches tabs, optionally shows the panel and focuses the terminal, and schedules an auto-hide (default 5 s, cancelled on any user interaction via `interactionBus`).
- **Drag-drop** (handled in `lib.rs` via `tauri://drag-drop`): folders become new sessions; files paste the quoted path into the active terminal.
- **Single-instance**: `tauri-plugin-single-instance` — a second launch just shows the panel of the running instance.
- **Hotkey**: configurable via `set_hotkey` / captured through `HotkeyCaptureModal`; previous binding is re-registered on failure to avoid leaving the user without a shortcut.

## Dependencies

### Frontend
- **React 19** — UI framework
- **xterm.js** + `@xterm/addon-fit` — terminal emulator
- **framer-motion** — animations
- **@tauri-apps/api** — IPC bridge
- **@tauri-apps/plugin-global-shortcut**, **@tauri-apps/plugin-updater**, **@tauri-apps/plugin-opener** — Tauri plugin JS bindings

### Backend (Rust)
- **tauri v2** — app framework (`tray-icon`, `image-png` features)
- **portable-pty** — cross-platform pseudoterminal
- **tauri-plugin-global-shortcut** — configurable hotkey
- **tauri-plugin-single-instance** — enforce one running instance
- **tauri-plugin-dialog** — folder picker
- **tauri-plugin-opener** — open URLs/paths
- **tauri-plugin-updater** — auto-update
- **rodio** — sound playback
- **regex**, **serde**, **uuid**, **dirs**, **base64**, **log**, **env_logger**
- Platform-specific: `windows` + `winreg` (Windows), `objc2` + `objc2-app-kit` (macOS)

## Key Directories

- `src/` — React frontend (components, hooks, lib)
- `src-tauri/src/` — Rust backend modules listed above
- `src-tauri/capabilities/` — Tauri v2 capability manifests
- `src-tauri/icons/` — app icons for all platforms
- `public/` — static assets; `notch.html` is the notch window's full UI
- `shelly-legacy/` — archived WPF/.NET codebase (reference only)
- `.github/workflows/` — CI/CD workflows
