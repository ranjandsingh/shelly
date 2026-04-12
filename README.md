# Shelly

A full-fledged, cross-platform floating terminal — with multi-session tabs, IDE project detection, theming, and built-in Claude Code status tracking. **Total download: ~5 MB.**

A small "notch" pill lives at the top-center of your screen. Click it — or press your global hotkey (<kbd>Ctrl</kbd>+<kbd>`</kbd> by default) — and it springs open into a compact terminal panel with embedded sessions. When a session's working directory contains a `CLAUDE.md`, Shelly can auto-run `claude --continue` after the prompt appears.

## Super lightweight

The entire app — installer, binary, assets, everything — is around **5 MB**. Tauri uses the OS's native webview instead of bundling Chromium, so Shelly stays small and starts fast. Most Electron-based terminals ship at 100+ MB; Shelly is a rounding error by comparison, with none of the features cut.

<sub>Originally inspired by <a href="https://github.com/adamlyttleapps/notchy">Notchy</a> for macOS.</sub>

## Features

### Window & panel
- **Floating panel** — always-on-top terminal, toggled with a global hotkey (default <kbd>Ctrl</kbd>+<kbd>`</kbd>, fully rebindable via an in-app capture dialog)
- **Notch pill** — always visible at the top-center (or bottom-center, togglable); click to expand, blur to hide. Notch-aware on macOS and DPI-aware on Windows so it sits flush with the screen edge
- **Pin mode** — lock the panel open so it doesn't hide on blur (handy when pasting, reading long output, or working alongside another window)
- **Panel transparency** — adjustable background opacity with an optional content-fade cascade so the terminal stays legible while the chrome recedes
- **Spring animation** — the panel grows from the notch's size and shrinks back on hide, with rounded corners animated during the transition
- **System tray** — tray icon with quick access to show/hide, check for updates, and exit

### Sessions
- **Multi-session tabs** — <kbd>Ctrl</kbd>+<kbd>T</kbd> new, <kbd>Ctrl</kbd>+<kbd>W</kbd> close, <kbd>Ctrl</kbd>+<kbd>Tab</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Tab</kbd> to cycle
- **Per-path tab color** — right-click a tab for a color palette; the color sticks to that folder and is reused on every future session for the same path
- **Auto-tinted duplicate tabs** — when two tabs share a folder, a left-edge strip disambiguates them at a glance
- **Rename tabs** — override the folder-derived title with anything you want
- **Session persistence** — optionally restore your tabs across restarts, with the previously active session auto-resuming
- **Recent folders** — chevron dropdown listing recently used working directories, with an open-indicator dot on rows already running

### Terminal
- **Shell selection** — PowerShell 7, Windows PowerShell, cmd, bash, zsh, WSL, and more (auto-detected; override per session or globally)
- **Drag & drop** — drop a folder onto the panel for a new session; drop a file to paste its quoted path into the active terminal
- **Folder picker** — "Open folder" button (with a recent-folder split) launches a native folder dialog
- **Live resize** — PTY dimensions follow the panel as you drag its edges

### Claude Code integration
- **Status tracking** — classifies each session as Idle / Working / Waiting for input / Task completed / Interrupted and surfaces live indicators on the notch
- **Attention flow** — pops the panel open when an agent needs you, then auto-hides. Configurable trigger states and delay
- **Auto-launch Claude** — runs `claude --continue` in directories containing a `CLAUDE.md` (opt-in, per-session skip)
- **IDE project detection** — parses open VS Code and JetBrains window titles to suggest projects you're already working on
- **Sound notifications** — audible alerts on state transitions, with per-state toggles
- **Sleep prevention** — holds an OS wake-lock while any session is actively working, so long tasks don't get paused by the OS suspending

### Theming
- **33 bundled themes** — curated VS Code palettes ready to go
- **VS Code theme import** — paste a VS Code theme JSON (or URL) and Shelly parses it into a full terminal palette
- **Font size & family** — adjustable in the themes modal, live-previewed
- **UI hints toggle** — show or hide the small keyboard-hint overlays once you've learned the shortcuts

### Platform & packaging
- **Cross-platform** — Windows, macOS, and Linux via Tauri v2
- **Single instance** — a second launch just shows the panel of the running one
- **Start with OS** — launches on login (opt-in)
- **Auto-update** — built-in updater with an in-app update banner and a tray "Check for updates" entry

## Install

Download the latest release from the [Releases](https://github.com/ranjandsingh/shelly/releases) page and run the installer. The entire download is about **5 MB**.

## Build from Source

Prerequisites:
- [Rust toolchain](https://rustup.rs/)
- [Bun](https://bun.sh/)
- Platform webview dependencies per [Tauri's setup guide](https://tauri.app/start/prerequisites/)

Dev mode (Vite dev server on port 1420 + Tauri):
```bash
bun install
bun run tauri dev
```

Production build:
```bash
bun run tauri build
```

Frontend-only check (type-check + Vite, no Tauri bundle):
```bash
bun run build
```

## Tech Stack

**Frontend** — React 19, TypeScript, Vite, [xterm.js](https://xtermjs.org/), framer-motion.
**Backend** — Rust, [Tauri v2](https://tauri.app/), [portable-pty](https://crates.io/crates/portable-pty), rodio.

See [CLAUDE.md](CLAUDE.md) for a full architectural breakdown.

## Keyboard Shortcuts

| Action | Shortcut |
| --- | --- |
| Show / hide panel | <kbd>Ctrl</kbd>+<kbd>`</kbd> (configurable) |
| New session | <kbd>Ctrl</kbd>+<kbd>T</kbd> |
| Close session | <kbd>Ctrl</kbd>+<kbd>W</kbd> |
| Next / previous tab | <kbd>Ctrl</kbd>+<kbd>Tab</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Tab</kbd> |

## License

[MIT](LICENSE)
