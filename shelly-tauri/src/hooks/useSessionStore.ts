import { useState, useEffect, useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";

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

  const addSession = useCallback(
    async (name?: string, projectPath?: string, workingDir?: string) => {
      const session = await invoke<TerminalSession>("add_session", {
        name: name ?? null,
        projectPath: projectPath ?? null,
        workingDir: workingDir ?? null,
      });
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
