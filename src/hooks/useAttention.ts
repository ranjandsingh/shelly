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
const ACTIVE_WORK_WINDOW_MS = 10_000;

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
  const lastInteractionTimeRef = useRef<number>(0);
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

  // Track last interaction time; cancel auto-hide and clear pending on any interaction.
  useEffect(() => {
    const unsub = onSessionInteraction(() => {
      lastInteractionTimeRef.current = Date.now();
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
      const isExpanded = panelVisibleRef.current;
      const userIsActive = (Date.now() - lastInteractionTimeRef.current) < ACTIVE_WORK_WINDOW_MS;

      if (isExpanded) {
        // Panel is already open — the status icon on the inactive tab signals the
        // alert without causing a tab-switch flicker. Don't touch tab focus.
        return;
      }

      if (userIsActive) {
        // User is actively working; store the session so opening the panel later
        // lands on the right tab, but don't interrupt them.
        pendingAttentionSessionRef.current = sessionId;
        return;
      }

      // Panel hidden + user idle: switch to the alerting session.
      if (activeSessionIdRef.current !== sessionId) {
        await selectSessionRef.current(sessionId);
      }

      if (s.stealFocus) {
        await invoke("show_panel").catch(() => {});
        requestAnimationFrame(() => focusActiveTerminal());

        // Reset and start auto-hide timer.
        if (timeoutRef.current !== null) {
          window.clearTimeout(timeoutRef.current);
        }
        timeoutRef.current = window.setTimeout(() => {
          timeoutRef.current = null;
          invoke("hide_panel").catch(() => {});
        }, s.autoHideTimeoutMs || DEFAULT_TIMEOUT_MS);
      } else {
        pendingAttentionSessionRef.current = sessionId;
      }
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
