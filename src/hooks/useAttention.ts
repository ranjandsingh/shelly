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
  const pendingAttentionSessionRef = useRef<string | null>(null);
  const settingsRef = useRef<AttentionSettings>({
    enabled: true,
    triggerStatuses: ["TaskCompleted", "WaitingForInput"],
    stealFocus: true,
    autoHideTimeoutMs: DEFAULT_TIMEOUT_MS,
  });
  const panelVisibleRef = useRef<boolean>(true);
  const activeSessionIdRef = useRef<string | null>(activeSessionId);
  // Stable refs so event listeners registered with [] deps always see the
  // latest callbacks without needing to re-register on every sessions change.
  const selectSessionRef = useRef(selectSession);
  const sessionExistsRef = useRef(sessionExists);

  useEffect(() => { selectSessionRef.current = selectSession; }, [selectSession]);
  useEffect(() => { sessionExistsRef.current = sessionExists; }, [sessionExists]);

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
      if (e.payload && !panelVisibleRef.current) {
        // Panel transitioning hidden→visible: switch to pending attention session if any.
        const pending = pendingAttentionSessionRef.current;
        if (pending && sessionExistsRef.current(pending)) {
          selectSessionRef.current(pending).catch(() => {});
        }
      }
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

  // Cancel auto-hide timer on any interaction; clear pending attention so we
  // don't forcibly re-switch tabs after the user has already handled it.
  useEffect(() => {
    const unsub = onSessionInteraction(() => {
      pendingAttentionSessionRef.current = null;
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
      if (!sessionExistsRef.current(sessionId)) return;

      const s = settingsRef.current;

      // Switch tab if needed.
      if (activeSessionIdRef.current !== sessionId) {
        await selectSessionRef.current(sessionId);
      }

      if (s.stealFocus) {
        // Show panel if hidden.
        if (!panelVisibleRef.current) {
          await invoke("show_panel").catch(() => {});
        }
        // Focus terminal after a frame so layout is ready.
        requestAnimationFrame(() => focusActiveTerminal());
      } else {
        // stealFocus is off — panel won't auto-show. Store the session so that
        // when the user manually opens the panel, it lands on the right tab.
        pendingAttentionSessionRef.current = sessionId;
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
  }, []);
}
