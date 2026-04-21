import { useEffect, useRef, useCallback } from "react";
import { Terminal } from "xterm";
import { FitAddon } from "@xterm/addon-fit";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { emitSessionInteraction } from "../lib/interactionBus";
import { registerTerminalFocus } from "../lib/terminalFocus";
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
  sessionId: string | null,
  workingDirectory?: string,
  theme?: any,
  fontSize?: number
) {
  const termRef = useRef<Terminal | null>(null);
  const fitAddonRef = useRef<FitAddon | null>(null);
  const unlistenRef = useRef<(() => void) | null>(null);
  const unlistenExitRef = useRef<(() => void) | null>(null);
  const resizeTimerRef = useRef<number | null>(null);
  const pollTimerRef = useRef<number | null>(null);
  const currentSessionRef = useRef<string | null>(null);
  const sessionExitedRef = useRef<boolean>(false);
  const lastWorkDirRef = useRef<string>("");
  const replayingRef = useRef<boolean>(false);
  // Increments on every attachSession call. Any in-flight attach whose gen
  // no longer matches aborts at the next await so StrictMode's double-mount
  // (and any rapid tab switch) can't race two concurrent attaches.
  const attachGenRef = useRef<number>(0);
  // Per-session last cols/rows we sent to Rust. Tracked per-session because
  // the xterm instance is shared across tabs — resizing while tab A is active
  // only resizes PTY A, so PTY B stays at its old dims until we reattach to it.
  // Also avoids spurious SIGWINCH (bash's readline on same-size resize can wipe
  // multi-line prompts down to just the last line).
  const sessionSizesRef = useRef<Map<string, { cols: number; rows: number }>>(new Map());
  // True while the floating panel is mid-animation (pill growing or shrinking).
  // We do NOT forward resizes during this phase because the pill container fits
  // xterm to ~18x1, which would SIGWINCH the shell into a single-line redraw.
  const panelAnimatingRef = useRef<boolean>(false);

  // Initialize xterm once, but only after the container has real dimensions.
  useEffect(() => {
    if (!containerRef.current || termRef.current) return;
    const container = containerRef.current;

    const term = new Terminal({
      theme: {
        background: "rgba(0, 0, 0, 0)",
        foreground: "#e6e6e6",
        cursor: "#e6e6e6",
        selectionBackground: "#44475a",
      },
      fontFamily: "Cascadia Code, Menlo, Consolas, monospace",
      fontSize: 11,
      cursorBlink: true,
      allowProposedApi: true,
      allowTransparency: true,
    });

    const fitAddon = new FitAddon();
    term.loadAddon(fitAddon);

    // Stash refs immediately so attachSession can see them; open() is deferred.
    termRef.current = term;
    fitAddonRef.current = fitAddon;

    let opened = false;
    const tryOpen = () => {
      if (opened) return;
      if (container.offsetWidth > 0 && container.offsetHeight > 0) {
        term.open(container);
        fitAddon.fit();
        opened = true;
        registerTerminalFocus(() => term.focus());
        initObserver.disconnect();
      }
    };

    const initObserver = new ResizeObserver(() => tryOpen());
    initObserver.observe(container);
    // Attempt immediately in case the container already has size.
    tryOpen();

    // Handle input
    term.onData((data) => {
      if (!currentSessionRef.current) return;
      if (replayingRef.current) return;

      if (sessionExitedRef.current && (data === "\r" || data === "\n")) {
        sessionExitedRef.current = false;
        const sid = currentSessionRef.current;
        const workDir = lastWorkDirRef.current || "";
        term.reset();
        fitAddon.fit();
        (async () => {
          try {
            if (unlistenRef.current) {
              unlistenRef.current();
              unlistenRef.current = null;
            }
            unlistenRef.current = (await onTerminalOutput(
              (event: TerminalOutputEvent) => {
                if (event.sessionId === sid) {
                  const bytes = Uint8Array.from(atob(event.data), (c) => c.charCodeAt(0));
                  term.write(bytes);
                }
              }
            )) as unknown as () => void;

            await createPty(sid, workDir, term.cols, term.rows);
            if (workDir) {
              setTimeout(async () => {
                try {
                  await invoke("send_startup_command", { sessionId: sid });
                } catch {}
              }, 1500);
            }
          } catch (e) {
            term.write(`\r\n\x1b[31mFailed to restart: ${e}\x1b[0m\r\n`);
          }
        })();
        return;
      }

      if (!sessionExitedRef.current) {
        writeInput(currentSessionRef.current, data);
      }
    });

    term.onKey(() => {
      const sid = currentSessionRef.current;
      if (sid) emitSessionInteraction(sid);
    });

    term.onScroll(() => {
      const sid = currentSessionRef.current;
      if (sid) emitSessionInteraction(sid);
    });

    // Only forward a resize to the PTY when cols/rows actually changed for
    // the current session. Same-size resize still triggers SIGWINCH on the
    // shell, which on MINGW bash redraws only the last line of a multi-line
    // prompt (wiping the banner from view). Also skip while the panel is
    // animating — mid-pill the container fits to ~18x1 which, if forwarded,
    // causes bash to redraw the prompt to a single-line state.
    const maybeResizePty = () => {
      const sid = currentSessionRef.current;
      if (!sid) return;
      if (panelAnimatingRef.current) return;
      const last = sessionSizesRef.current.get(sid);
      if (last && last.cols === term.cols && last.rows === term.rows) return;
      sessionSizesRef.current.set(sid, { cols: term.cols, rows: term.rows });
      resizeTerminal(sid, term.cols, term.rows);
    };

    // Debounced resize — only runs after xterm is open
    const resizeObserver = new ResizeObserver(() => {
      if (!opened) return;
      if (resizeTimerRef.current) clearTimeout(resizeTimerRef.current);
      resizeTimerRef.current = window.setTimeout(() => {
        fitAddon.fit();
        maybeResizePty();
      }, 150);
    });
    resizeObserver.observe(container);

    const mouseDownHandler = () => {
      const sid = currentSessionRef.current;
      if (sid) emitSessionInteraction(sid);
    };
    container.addEventListener("mousedown", mouseDownHandler);

    // Re-fit + force-paint after panel animation ends
    let unlistenAnim: (() => void) | null = null;
    listen<boolean>("panel-animating", (e) => {
      if (e.payload) {
        panelAnimatingRef.current = true;
      } else {
        setTimeout(() => {
          if (!opened) tryOpen();
          fitAddon.fit();
          try { term.refresh(0, term.rows - 1); } catch {}
          // Clear the flag right before maybeResizePty so the post-animation
          // fit can send a real resize if the final size genuinely changed.
          panelAnimatingRef.current = false;
          maybeResizePty();
        }, 50);
      }
    }).then((fn) => { unlistenAnim = fn as unknown as () => void; });

    term.textarea?.addEventListener("paste", (e) => {
      e.preventDefault();
      e.stopImmediatePropagation();
    }, { capture: true });

    term.attachCustomKeyEventHandler((e) => {
      const mod = e.ctrlKey || e.metaKey;
      if (mod && e.key === "v" && e.type === "keydown") {
        navigator.clipboard.readText().then((text) => {
          if (currentSessionRef.current) {
            writeInput(currentSessionRef.current, text);
          }
        });
        return false;
      }
      if (mod && e.key === "c" && e.type === "keydown") {
        if (term.hasSelection()) {
          navigator.clipboard.writeText(term.getSelection());
          term.clearSelection();
          return false;
        }
      }
      return true;
    });

    return () => {
      initObserver.disconnect();
      resizeObserver.disconnect();
      container.removeEventListener("mousedown", mouseDownHandler);
      unlistenAnim?.();
      registerTerminalFocus(null);
      term.dispose();
      termRef.current = null;
      fitAddonRef.current = null;
    };
  }, [containerRef]);

  // Apply theme and font size changes
  useEffect(() => {
    const term = termRef.current;
    if (!term) return;
    if (theme) term.options.theme = theme;
    if (fontSize) term.options.fontSize = fontSize;
    fitAddonRef.current?.fit();
  }, [theme, fontSize]);

  // Attach to session
  const attachSession = useCallback(
    async (newSessionId: string, workDir: string) => {
      const gen = ++attachGenRef.current;
      const isStale = () => gen !== attachGenRef.current;

      const term = termRef.current;
      const fitAddon = fitAddonRef.current;
      if (!term || !fitAddon) return;

      // Cleanup previous listeners
      if (unlistenRef.current) {
        unlistenRef.current();
        unlistenRef.current = null;
      }
      if (unlistenExitRef.current) {
        unlistenExitRef.current();
        unlistenExitRef.current = null;
      }

      currentSessionRef.current = newSessionId;
      sessionExitedRef.current = false;
      lastWorkDirRef.current = workDir;

      const exists = await checkHasTerminal(newSessionId);
      if (isStale()) return;

      // For a fresh session, spawn the PTY first. Its output accumulates in the
      // Rust-side buffer (pty.rs tracks last_emitted), so nothing is lost even
      // if the shell prints its banner before we finish wiring listeners.
      if (!exists) {
        await createPty(newSessionId, workDir, term.cols, term.rows);
        if (isStale()) return;
        // PTY was born at exactly these dimensions; seed the dedup cache so
        // the first resize observer fire doesn't re-send the same size and
        // trigger a SIGWINCH-driven prompt redraw.
        sessionSizesRef.current.set(newSessionId, { cols: term.cols, rows: term.rows });
      } else {
        // Reattaching to an existing PTY. The shared xterm may have been
        // resized while a different tab was active, so PTY's recorded dims
        // can be stale. Sync them now — otherwise this shell wraps lines at
        // the old cols while xterm renders at new cols, which breaks the
        // display and makes input land at the wrong column positions.
        const prev = sessionSizesRef.current.get(newSessionId);
        if (!panelAnimatingRef.current &&
            (!prev || prev.cols !== term.cols || prev.rows !== term.rows)) {
          await resizeTerminal(newSessionId, term.cols, term.rows);
          if (isStale()) return;
          sessionSizesRef.current.set(newSessionId, { cols: term.cols, rows: term.rows });
        }
      }

      // Pause live emits while we attach. Anything accumulating in output_buffer
      // during this window is replayed when we flip suppress back off.
      await suppressLiveOutput(newSessionId, true);
      if (isStale()) return;

      // Only suppress xterm's onData (CPR responses etc.) while replaying OLD
      // buffered data. For a fresh PTY the "buffered" bytes are live — the
      // shell is actively blocked on read() waiting for the CPR answer, so
      // the response has to flow back.
      replayingRef.current = exists;
      term.reset();
      if (term.element) fitAddon.fit();

      // Register the live listener now (still suppressed — no events flow yet).
      const liveUnlisten = (await onTerminalOutput(
        (event: TerminalOutputEvent) => {
          if (event.sessionId === newSessionId) {
            const bytes = Uint8Array.from(atob(event.data), (c) =>
              c.charCodeAt(0)
            );
            term.write(bytes);
          }
        }
      )) as unknown as () => void;
      if (isStale()) {
        liveUnlisten();
        return;
      }
      unlistenRef.current = liveUnlisten;

      // Pull everything emitted so far. get_buffered_output advances
      // last_emitted so future live events carry only the delta.
      const b64 = await getBufferedOutput(newSessionId);
      if (isStale()) return;
      if (b64) {
        const bytes = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
        term.write(bytes);
      }
      await new Promise((r) => setTimeout(r, 50));
      if (isStale()) return;
      if (exists) replayingRef.current = false;

      // Flip suppression off. The backend emits any delta that accumulated
      // between the fetch above and now, then live emits resume.
      await suppressLiveOutput(newSessionId, false);
      if (isStale()) return;

      // xterm.write is async — the replayed buffer and any post-suppress
      // delta may still be rendering. Queue scrollToBottom via a write
      // callback so it runs after the pipeline drains. Without this, fit()
      // and reset() during attach can leave the viewport anchored above the
      // last line even though the cursor is at the bottom.
      term.write("", () => { term.scrollToBottom(); });

      if (!exists && workDir) {
        // After shell prompt appears, optionally auto-launch claude
        setTimeout(async () => {
          try {
            await invoke("send_startup_command", { sessionId: newSessionId });
          } catch (e) {
            console.warn("[useTerminal] send_startup_command failed:", e);
          }
        }, 1500);
      }

      // Listen for process exit
      const exitUnlisten = (await listen<string>(
        "process-exited",
        (event) => {
          if (event.payload === newSessionId && termRef.current) {
            sessionExitedRef.current = true;
            termRef.current.write(
              "\r\n\x1b[90m[Process exited. Press Enter to restart]\x1b[0m\r\n"
            );
          }
        }
      )) as unknown as () => void;
      if (isStale()) { exitUnlisten(); return; }
      unlistenExitRef.current = exitUnlisten;

      term.focus();

      // Start 500ms status polling
      if (pollTimerRef.current) clearInterval(pollTimerRef.current);
      pollTimerRef.current = window.setInterval(async () => {
        const t = termRef.current;
        const sid = currentSessionRef.current;
        if (!t || !sid) return;
        const buffer = t.buffer.active;
        const rows = t.rows;
        const baseY = buffer.baseY;
        const lines: string[] = [];
        for (let i = baseY; i < baseY + rows; i++) {
          const line = buffer.getLine(i);
          if (line) lines.push(line.translateToString(true));
        }
        const text = lines.join("\n");
        await invoke("parse_visible_text", { sessionId: sid, text });
      }, 500);
    },
    []
  );

  // React to session changes
  useEffect(() => {
    if (sessionId) {
      const workDir = workingDirectory || "";
      attachSession(sessionId, workDir);
    }
    return () => {
      if (unlistenRef.current) {
        unlistenRef.current();
        unlistenRef.current = null;
      }
      if (unlistenExitRef.current) {
        unlistenExitRef.current();
        unlistenExitRef.current = null;
      }
      if (pollTimerRef.current) {
        clearInterval(pollTimerRef.current);
        pollTimerRef.current = null;
      }
    };
  }, [sessionId, workingDirectory, attachSession]);

  return { term: termRef, fitAddon: fitAddonRef };
}
