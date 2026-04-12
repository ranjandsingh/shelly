# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Build

**Frontend + Tauri (dev mode):**
```bash
bun install
bun run tauri dev
```

**Production build:**
```bash
bun run tauri build
```

There are no tests configured yet.

## Commit Guidelines

Do not add `Co-Authored-By` lines to commit messages.

Do not commit anything under `docs/superpowers/` (specs, plans, and other workflow artifacts). That directory is gitignored and kept local only.

## Overview

Shelly is a cross-platform system tray app built with Tauri v2 that provides a floating terminal panel at the top-center of the screen, with automatic IDE project detection. When the user clicks the tray icon or presses Ctrl+\`, a floating panel appears with embedded terminal sessions (xterm.js frontend, portable-pty backend) that auto-`cd` into detected IDE project directories and launch `claude`.

## Architecture

**Frontend** (React + Vite + TypeScript): `src/` contains the UI layer rendered inside Tauri webview windows. `App.tsx` is the main component. Terminal rendering uses xterm.js via `TerminalView.tsx`.

**Backend** (Rust / Tauri v2): `src-tauri/src/` contains the native backend. `lib.rs` is the main entry point registering Tauri commands and plugins.

- **PTY**: `pty.rs` wraps `portable-pty` to spawn shell processes with a pseudoterminal. Data flows bidirectionally via Tauri IPC commands.
- **Session management**: `session_store.rs` holds terminal sessions and coordinates with `ide_detector.rs` to discover open IDE projects.
- **Status detection**: `status_parser.rs` classifies terminal output into states (Working, WaitingForInput, Interrupted, Idle).
- **Tray**: `tray.rs` manages the system tray icon and menu.
- **Settings**: `settings.rs` persists user preferences to disk.
- **Auto-start**: `auto_start.rs` manages OS-level startup registration.
- **IDE detection**: `ide_detector.rs` parses window titles to detect VS Code and JetBrains IDE projects.

**IPC**: Frontend communicates with the Rust backend via Tauri's `invoke()` command system defined in `src/lib/ipc.ts`.

## Dependencies

### Frontend
- **React 19** — UI framework
- **xterm.js** — terminal emulator
- **framer-motion** — animations
- **@tauri-apps/api** — Tauri IPC bridge
- **@tauri-apps/plugin-updater** — auto-update support

### Backend (Rust)
- **tauri v2** — app framework (tray-icon, image-png)
- **portable-pty** — cross-platform pseudoterminal
- **tauri-plugin-global-shortcut** — hotkey registration
- **tauri-plugin-updater** — auto-update
- **rodio** — sound playback

## Key Directories

- `src/` — React frontend (components, hooks, lib)
- `src-tauri/` — Rust backend (Tauri commands, PTY, session management)
- `src-tauri/icons/` — app icons for all platforms
- `public/` — static assets (notch window HTML, icons)
- `shelly-legacy/` — archived WPF/.NET codebase (for reference/patching)
- `.github/workflows/` — CI/CD workflows
