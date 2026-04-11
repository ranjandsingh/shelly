import { useState, useEffect, useCallback, useRef } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

export interface TerminalSession {
  id: string;
  projectName: string;
  projectPath: string | null;
  workingDirectory: string;
  hasStarted: boolean;
  status: string;
  isActive: boolean;
  skipAutoLaunch: boolean;
}

export function useSessionStore() {
  const [sessions, setSessions] = useState<TerminalSession[]>([]);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const pendingStatusRef = useRef<Map<string, string>>(new Map());
  const debounceTimerRef = useRef<number | null>(null);

  const refresh = useCallback(async () => {
    const s = await invoke<TerminalSession[]>("get_sessions");
    const active = await invoke<string | null>("get_active_session_id");
    setSessions(s);
    setActiveSessionId(active);
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  // Listen for status changes from Rust (debounced to reduce re-renders)
  useEffect(() => {
    const unlisten = listen<{ sessionId: string; status: string }>(
      "status-changed",
      (event) => {
        pendingStatusRef.current.set(
          event.payload.sessionId,
          event.payload.status
        );
        if (debounceTimerRef.current !== null) {
          clearTimeout(debounceTimerRef.current);
        }
        debounceTimerRef.current = window.setTimeout(() => {
          const pending = new Map(pendingStatusRef.current);
          pendingStatusRef.current.clear();
          debounceTimerRef.current = null;
          setSessions((prev) =>
            prev.map((s) => {
              const newStatus = pending.get(s.id);
              return newStatus ? { ...s, status: newStatus } : s;
            })
          );
        }, 200);
      }
    );
    return () => {
      if (debounceTimerRef.current !== null) {
        clearTimeout(debounceTimerRef.current);
      }
      unlisten.then((f) => f());
    };
  }, []);

  // Listen for process-exited events
  useEffect(() => {
    const unlisten = listen<string>("process-exited", (event) => {
      const exitedId = event.payload;
      setSessions((prev) =>
        prev.map((s) =>
          s.id === exitedId ? { ...s, status: "Exited", hasStarted: false } : s
        )
      );
    });
    return () => {
      unlisten.then((f) => f());
    };
  }, []);

  const addSession = useCallback(
    async (name?: string, projectPath?: string, workingDir?: string) => {
      const session = await invoke<TerminalSession>("add_session", {
        name: name ?? null,
        projectPath: projectPath ?? null,
        workingDir: workingDir ?? null,
      });
      // Select the new session so it gets focused
      await invoke("select_session", { sessionId: session.id });
      await refresh();
      return session;
    },
    [refresh]
  );

  const selectSession = useCallback(
    async (id: string) => {
      await invoke("select_session", { sessionId: id });
      await refresh();
    },
    [refresh]
  );

  const removeSession = useCallback(
    async (id: string) => {
      await invoke<string | null>("remove_session", { sessionId: id });
      await refresh();
    },
    [refresh]
  );

  const renameSession = useCallback(
    async (id: string, name: string) => {
      await invoke("rename_session", { sessionId: id, name });
      await refresh();
    },
    [refresh]
  );

  return {
    sessions,
    activeSessionId,
    addSession,
    selectSession,
    removeSession,
    renameSession,
    refresh,
  };
}
