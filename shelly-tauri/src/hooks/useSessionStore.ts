import { useState, useEffect, useCallback } from "react";
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

  const refresh = useCallback(async () => {
    const s = await invoke<TerminalSession[]>("get_sessions");
    const active = await invoke<string | null>("get_active_session_id");
    setSessions(s);
    setActiveSessionId(active);
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  // Listen for status changes from Rust
  useEffect(() => {
    const unlisten = listen<{ sessionId: string; status: string }>(
      "status-changed",
      (event) => {
        setSessions((prev) =>
          prev.map((s) =>
            s.id === event.payload.sessionId
              ? { ...s, status: event.payload.status }
              : s
          )
        );
      }
    );
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
