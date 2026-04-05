import { useEffect, useMemo, useState, useCallback } from "react";
import { TerminalView } from "./components/TerminalView";
import { SessionTabBar } from "./components/SessionTabBar";
import { FloatingPanel } from "./components/FloatingPanel";
import { useSessionStore } from "./hooks/useSessionStore";
import { listen } from "@tauri-apps/api/event";
import "./App.css";

function App() {
  const {
    sessions,
    activeSessionId,
    addSession,
    selectSession,
    removeSession,
  } = useSessionStore();

  const [isExpanded, setIsExpanded] = useState(false);

  const activeSession = useMemo(
    () => sessions.find((s) => s.id === activeSessionId),
    [sessions, activeSessionId]
  );

  const togglePanel = useCallback(() => {
    setIsExpanded((prev) => !prev);
  }, []);

  const handleExpand = useCallback((_pin: boolean) => {
    setIsExpanded(true);
  }, []);

  const handleCollapse = useCallback(() => {
    setIsExpanded(false);
  }, []);

  // Listen for tray events
  useEffect(() => {
    const unlistenToggle = listen("tray-toggle-panel", () => {
      togglePanel();
    });
    const unlistenNewSession = listen("tray-new-session", () => {
      addSession();
    });
    return () => {
      unlistenToggle.then((f) => f());
      unlistenNewSession.then((f) => f());
    };
  }, [addSession, togglePanel]);

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
    <FloatingPanel
      sessions={sessions}
      isExpanded={isExpanded}
      onExpand={handleExpand}
      onCollapse={handleCollapse}
    >
      <div className="app-content">
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
    </FloatingPanel>
  );
}

export default App;
