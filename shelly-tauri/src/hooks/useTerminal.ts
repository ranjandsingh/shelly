import { useEffect, useRef, useCallback } from "react";
import { Terminal } from "xterm";
import { FitAddon } from "@xterm/addon-fit";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
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
      if (!currentSessionRef.current) return;

      // If process exited and user presses Enter, restart the terminal
      if (sessionExitedRef.current && (data === "\r" || data === "\n")) {
        sessionExitedRef.current = false;
        const sid = currentSessionRef.current;
        const workDir = lastWorkDirRef.current || "";
        term.reset();
        fitAddon.fit();
        (async () => {
          try {
            // Re-subscribe to output first
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

    // Handle resize (debounced)
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
      observer.disconnect();
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
      console.log(`[useTerminal] attachSession: ${newSessionId}, workDir=${workDir}`);
      const term = termRef.current;
      const fitAddon = fitAddonRef.current;
      if (!term || !fitAddon) {
        console.warn("[useTerminal] attachSession: term or fitAddon not ready");
        return;
      }

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
      console.log(`[useTerminal] hasTerminal=${exists}`);

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
        unlistenRef.current = (await onTerminalOutput(
          (event: TerminalOutputEvent) => {
            if (event.sessionId === newSessionId) {
              const bytes = Uint8Array.from(atob(event.data), (c) =>
                c.charCodeAt(0)
              );
              term.write(bytes);
            }
          }
        )) as unknown as () => void;

        await suppressLiveOutput(newSessionId, false);
      } else {
        // New terminal
        term.reset();
        fitAddon.fit();

        // Subscribe to live output first
        unlistenRef.current = (await onTerminalOutput(
          (event: TerminalOutputEvent) => {
            if (event.sessionId === newSessionId) {
              const bytes = Uint8Array.from(atob(event.data), (c) =>
                c.charCodeAt(0)
              );
              term.write(bytes);
            }
          }
        )) as unknown as () => void;

        // Create PTY at current xterm size (shell starts in workDir via cmd.cwd)
        await createPty(newSessionId, workDir, term.cols, term.rows);

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
      unlistenExitRef.current = (await listen<string>(
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
