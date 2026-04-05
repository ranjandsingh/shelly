import { useEffect, useMemo } from "react";
import { TerminalView } from "./components/TerminalView";
import { SessionTabBar } from "./components/SessionTabBar";
import { useSessionStore } from "./hooks/useSessionStore";
import { getCurrentWindow } from "@tauri-apps/api/window";
import "./App.css";

function App() {
  const {
    sessions,
    activeSessionId,
    addSession,
    selectSession,
    removeSession,
  } = useSessionStore();

  const activeSession = useMemo(
    () => sessions.find((s) => s.id === activeSessionId),
    [sessions, activeSessionId]
  );

  useEffect(() => {
    getCurrentWindow().show();
  }, []);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const mod = e.ctrlKey || e.metaKey;
      if (mod && e.key === "t") {
        e.preventDefault();
        addSession();
      }
      if (mod && e.key === "w") {
        e.preventDefault();
        if (activeSessionId && sessions.length > 1) {
          removeSession(activeSessionId);
        }
      }
      if (mod && e.key === "Tab") {
        e.preventDefault();
        if (sessions.length < 2) return;
        const idx = sessions.findIndex((s) => s.id === activeSessionId);
        const next = e.shiftKey
          ? (idx - 1 + sessions.length) % sessions.length
          : (idx + 1) % sessions.length;
        selectSession(sessions[next].id);
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [sessions, activeSessionId, addSession, selectSession, removeSession]);

  return (
    <div className="app">
      <SessionTabBar
        sessions={sessions}
        activeSessionId={activeSessionId}
        onSelect={selectSession}
        onAdd={() => addSession()}
        onClose={removeSession}
      />
      <TerminalView
        sessionId={activeSessionId}
        workingDirectory={activeSession?.workingDirectory}
      />
    </div>
  );
}

export default App;
