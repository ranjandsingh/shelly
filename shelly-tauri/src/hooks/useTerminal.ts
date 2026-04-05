import { useEffect, useRef, useCallback } from "react";
import { Terminal } from "xterm";
import { FitAddon } from "@xterm/addon-fit";
import { invoke } from "@tauri-apps/api/core";
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
  workingDirectory?: string
) {
  const termRef = useRef<Terminal | null>(null);
  const fitAddonRef = useRef<FitAddon | null>(null);
  const unlistenRef = useRef<(() => void) | null>(null);
  const resizeTimerRef = useRef<number | null>(null);
  const pollTimerRef = useRef<number | null>(null);
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

  // Attach to session
  const attachSession = useCallback(
    async (newSessionId: string, workDir: string) => {
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

        // Create PTY at current xterm size
        await createPty(newSessionId, workDir, term.cols, term.rows);
      }

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
      if (pollTimerRef.current) {
        clearInterval(pollTimerRef.current);
        pollTimerRef.current = null;
      }
    };
  }, [sessionId, workingDirectory, attachSession]);

  return { term: termRef, fitAddon: fitAddonRef };
}
