<p align="center">
  <img src="banner.png" alt="Shelly" width="700" />
</p>

# Shelly

A floating terminal that stays out of your way until you need it. Multi-session tabs, IDE project detection, themes, and built-in Claude Code status tracking — in a ~5 MB download, on MacOS and Windows.

A small "notch" pill sits at the top of your screen. Click it, or hit your rebindable global hotkey (Ctrl+\` by default), and it springs open into a terminal panel. Open a folder that has a `CLAUDE.md` and Shelly can auto-run `claude --continue` for you.

<sub>The original Shelly was a WPF app for Windows and kept a small footprint; this rewrite keeps it tiny and brings it to MacOS too. Originally inspired by <a href="https://github.com/adamlyttleapps/notchy">Notchy</a>.</sub>

## Tiny

The whole install is about **5 MB**. Installer, binary, assets, all of it. It launches fast and doesn't camp on your RAM.

## Features

### Panel
- Always-on-top floating terminal, toggled by a rebindable global hotkey (<kbd>Ctrl</kbd>+<kbd>&#96;</kbd> by default)
- Notch pill that can live at the top or bottom of the screen, hugging the real notch on MacOS and playing nice with Windows DPI
- Pin it open, or let it hide on blur
- Adjustable transparency with an optional content fade
- Springy grow/shrink animation from the pill
- System tray with show/hide, update-check, and quit

### Tabs & sessions
- Multi-session tabs — <kbd>Ctrl</kbd>+<kbd>T</kbd>, <kbd>Ctrl</kbd>+<kbd>W</kbd>, <kbd>Ctrl</kbd>+<kbd>Tab</kbd>
- Right-click a tab to color it; the color sticks to that folder for next time
- Duplicate folders get an auto-tinted left strip
- Rename any tab, or let it default to a capitalized folder name
- Optional session persistence — last-active tab auto-resumes on restart
- Recent folders dropdown with an open-indicator dot

### Terminal
- Shells auto-detected — PowerShell, cmd, bash, zsh, WSL, and friends
- Drag a folder in for a new session; drop a file to paste its quoted path
- Native folder picker with a recent-folder split button
- Live PTY resize as you drag the panel

### Claude Code integration
- Idle / Working / Waiting / Completed / Interrupted status, with live indicators on the notch
- Attention flow — the panel pops open when an agent needs you, then auto-hides
- Auto-launch `claude --continue` in directories with a `CLAUDE.md` (opt-in)
- IDE project detection from VS Code and JetBrains window titles
- Sounds on state transitions, with per-state toggles
- Sleep prevention while a session is working

### Themes
- 33 bundled themes, plus VS Code theme import (paste JSON or a URL)
- Live font size and family
- Optional hint overlays

### Platform & updates
- MacOS and Windows
- Single-instance — a second launch just pops the running one
- Start with OS (opt-in)
- Auto-update

## Install

Grab the latest installer from the [Releases](https://github.com/ranjandsingh/shelly/releases) page. About **5 MB**, then you're done.

## Build from Source

You'll want [Rust](https://rustup.rs/), [Bun](https://bun.sh/), and your platform's [Tauri prerequisites](https://tauri.app/start/prerequisites/).

```bash
bun install
bun run tauri dev        # dev mode, Vite on :1420
bun run tauri build      # production bundle
bun run build            # frontend-only check (tsc + Vite)
```

## Tech Stack

React 19 + TypeScript + [xterm.js](https://xtermjs.org/) on the frontend, Rust + [Tauri v2](https://tauri.app/) + [portable-pty](https://crates.io/crates/portable-pty) on the backend.

[CLAUDE.md](CLAUDE.md) has the full architectural breakdown.

## Keyboard Shortcuts

| Action | Shortcut |
| --- | --- |
| Show / hide panel | <kbd>Ctrl</kbd>+<kbd>&#96;</kbd> (configurable) |
| New session | <kbd>Ctrl</kbd>+<kbd>T</kbd> |
| Close session | <kbd>Ctrl</kbd>+<kbd>W</kbd> |
| Next / previous tab | <kbd>Ctrl</kbd>+<kbd>Tab</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Tab</kbd> |

## License

[MIT](LICENSE)
