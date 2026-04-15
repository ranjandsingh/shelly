<p align="center">
<img src="banner.png" alt="Shelly" width="700" />
</p>

# Shelly

I built Shelly because I wanted a terminal that just... gets out of the way. No giant windows, no massive memory footprint. Just a tiny "notch" pill at the top of your screen that springs open when you need it, and hides when you don't.

It handles multi-session tabs, detects your IDE projects, looks great with custom themes, and tracks your Claude Code status right from the notch. Oh, and the whole thing is roughly a **5 MB download**. Available for both MacOS and Windows.

> **A little backstory:** The original Shelly was a tiny WPF app I built just for Windows (inspired by [Notchy](https://github.com/adamlyttleapps/notchy)). I missed having it on my Mac, so I rewrote the whole thing from the ground up to be cross-platform while keeping the footprint incredibly small.

-----

## 🪶 Tiny by Design

Bloat is the enemy. The entire install—binary, assets, everything—is around 5 MB. It launches instantly and won't hog your RAM while sitting in the background.

## ✨ Why I love using it (Features)

### The Panel

  * **Always there, never in the way:** It’s a floating terminal toggled by a global hotkey (default is <kbd>Ctrl</kbd>+<kbd>&#96;</kbd>).
  * **The Pill:** A tiny notch that lives at the top (or bottom) of your screen. It hugs the physical notch on Mac and respects Windows DPI scaling.
  * **Smooth UX:** Springy grow/shrink animations, adjustable transparency, and the option to pin it open or let it hide automatically when you click away.

### Tabs & Sessions that make sense

  * **Multi-session:** The usual suspects work perfectly (<kbd>Ctrl</kbd>+<kbd>T</kbd>, <kbd>Ctrl</kbd>+<kbd>W</kbd>, <kbd>Ctrl</kbd>+<kbd>Tab</kbd>).
  * **Color coding:** Right-click a tab to give it a color. The best part? Shelly remembers that color the next time you open that specific folder.
  * **Smart tabs:** Duplicate folders get an auto-tinted strip so you don't get lost. You can rename tabs manually, or just let Shelly capitalize the folder name for you.
  * **Persistence:** Close the app, and your last-active tab will auto-resume right where you left off when you restart.

### A Proper Terminal

  * **Auto-detects your shell:** PowerShell, cmd, bash, zsh, WSL—it knows what you're running.
  * **Drag & Drop:** Drag a folder in to start a new session, or drop a file to instantly paste its quoted path.
  * **Live PTY:** Smooth, live terminal resizing as you drag the panel around.

### 🤖 Claude Code Integration

This is the killer feature for my workflow. If you use Claude Code, Shelly acts as a smart companion:

  * **Live Status:** The notch gives you visual indicators if Claude is Idle, Working, Waiting, Completed, or Interrupted.
  * **Attention Flow:** If an agent needs your input, the panel automatically pops open. Once you're done, it gets out of your way again.
  * **Auto-run:** Open a folder with a `CLAUDE.md` file, and Shelly can automatically fire up `claude --continue`.
  * **IDE Context:** It detects what you're working on by reading VS Code and JetBrains window titles.
  * **No sleeping on the job:** Shelly prevents your computer from going to sleep while a session is actively working.

### Make it Yours

  * Comes with **33 bundled themes**.
  * Already have a favorite VS Code theme? Just paste the JSON or URL to import it.
  * Live font size and family adjustments, plus optional hint overlays.

-----

## 🚀 Get Started

No bloated installers here. Just grab the latest ~5 MB release for your OS from the [Releases](https://github.com/ranjandsingh/shelly/releases) page and you're good to go.

## 🛠️ Building from Source

Want to tinker? You'll need [Rust](https://rustup.rs/), [Bun](https://bun.sh/), and your OS's [Tauri prerequisites](https://tauri.app/start/prerequisites/).

```bash
bun install
bun run tauri dev        # Dev mode (Vite runs on :1420)
bun run tauri build      # Build the production bundle
bun run build            # Frontend-only check (tsc + Vite)
```

## 💻 Under the Hood

I kept the stack modern and fast:

  * **Frontend:** React 19 + TypeScript + [xterm.js](https://xtermjs.org/)
  * **Backend:** Rust + [Tauri v2](https://tauri.app/) + [portable-pty](https://crates.io/crates/portable-pty)

*(If you want to geek out on the architecture, check out the full breakdown in [CLAUDE.md](CLAUDE.md)).*

## ⌨️ Shortcuts

| Action | Shortcut |
| :--- | :--- |
| **Show / Hide** | <kbd>Ctrl</kbd>+<kbd>&#96;</kbd> *(Configurable)* |
| **New Session** | <kbd>Ctrl</kbd>+<kbd>T</kbd> |
| **Close Session** | <kbd>Ctrl</kbd>+<kbd>W</kbd> |
| **Cycle Tabs** | <kbd>Ctrl</kbd>+<kbd>Tab</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Tab</kbd> |

-----

**License:** [MIT](https://www.google.com/search?q=LICENSE)