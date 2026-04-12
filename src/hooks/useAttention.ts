import { useEffect, useRef } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { focusActiveTerminal } from "../lib/terminalFocus";
import { onSessionInteraction } from "../lib/interactionBus";

interface AttentionSettings {
  enabled: boolean;
  triggerStatuses: string[];
  stealFocus: boolean;
  autoHideTimeoutMs: number;
}

interface AttentionEvent {
  sessionId: string;
  status: string;
}

const DEFAULT_TIMEOUT_MS = 5000;

export function useAttention(
  activeSessionId: string | null,
  selectSession: (id: string) => Promise<void>,
  sessionExists: (id: string) => boolean,
) {
  const timeoutRef = useRef<number | null>(null);
  const settingsRef = useRef<AttentionSettings>({
    enabled: true,
    triggerStatuses: ["TaskCompleted", "WaitingForInput"],
    stealFocus: true,
    autoHideTimeoutMs: DEFAULT_TIMEOUT_MS,
  });
  const panelVisibleRef = useRef<boolean>(true);
  const activeSessionIdRef = useRef<string | null>(activeSessionId);

  // Load settings once + refresh on any change command (simple approach: poll on mount).
  useEffect(() => {
    invoke<AttentionSettings>("get_attention_settings")
      .then((s) => {
        settingsRef.current = s;
      })
      .catch(() => {});
  }, []);

  // Track panel visibility via existing event stream.
  useEffect(() => {
    const unlisten = listen<boolean>("panel-visibility", (e) => {
      panelVisibleRef.current = !!e.payload;
      if (!e.payload && timeoutRef.current !== null) {
        window.clearTimeout(timeoutRef.current);
        timeoutRef.current = null;
      }
    });
    return () => {
      unlisten.then((f) => f());
    };
  }, []);

  // Cancel auto-hide timer on any interaction.
  useEffect(() => {
    const unsub = onSessionInteraction(() => {
      if (timeoutRef.current !== null) {
        window.clearTimeout(timeoutRef.current);
        timeoutRef.current = null;
      }
    });
    return () => unsub();
  }, []);

  // Keep an up-to-date ref of activeSessionId for the listener.
  useEffect(() => {
    activeSessionIdRef.current = activeSessionId;
  }, [activeSessionId]);

  // Main attention listener.
  useEffect(() => {
    const unlisten = listen<AttentionEvent>("attention-required", async (e) => {
      const { sessionId } = e.payload;
      if (!sessionExists(sessionId)) return;

      const s = settingsRef.current;

      // Switch tab if needed.
      if (activeSessionIdRef.current !== sessionId) {
        await selectSession(sessionId);
      }

      if (s.stealFocus) {
        // Show panel if hidden.
        if (!panelVisibleRef.current) {
          await invoke("show_panel").catch(() => {});
        }
        // Focus terminal after a frame so layout is ready.
        requestAnimationFrame(() => focusActiveTerminal());
      }

      // Reset and start auto-hide timer.
      if (timeoutRef.current !== null) {
        window.clearTimeout(timeoutRef.current);
      }
      timeoutRef.current = window.setTimeout(() => {
        timeoutRef.current = null;
        invoke("hide_panel").catch(() => {});
      }, s.autoHideTimeoutMs || DEFAULT_TIMEOUT_MS);
    });

    return () => {
      unlisten.then((f) => f());
      if (timeoutRef.current !== null) {
        window.clearTimeout(timeoutRef.current);
        timeoutRef.current = null;
      }
    };
  }, [selectSession, sessionExists]);
}
