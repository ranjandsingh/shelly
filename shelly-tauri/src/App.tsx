import { useEffect, useMemo, useState } from "react";
import { TerminalView } from "./components/TerminalView";
import { SessionTabBar } from "./components/SessionTabBar";
import { useSessionStore } from "./hooks/useSessionStore";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { DragBar } from "./components/DragBar";
import "./App.css";

function App() {
  const {
    sessions,
    activeSessionId,
    addSession,
    selectSession,
    removeSession,
  } = useSessionStore();

  const [_isVisible, setIsVisible] = useState(false);

  const activeSession = useMemo(
    () => sessions.find((s) => s.id === activeSessionId),
    [sessions, activeSessionId]
  );

  // Sync visibility state with Rust events
  useEffect(() => {
    const unlisten = listen<boolean>("panel-visibility", (event) => {
      setIsVisible(event.payload);
    });
    // Check initial visibility
    getCurrentWindow().isVisible().then(setIsVisible);
    return () => { unlisten.then((f) => f()); };
  }, []);

  // Keyboard shortcuts — capture phase to intercept before xterm
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const mod = e.ctrlKey || e.metaKey;
      if (!mod) return;

      if (e.key === "t") {
        e.preventDefault();
        e.stopPropagation();
        addSession();
      } else if (e.key === "w") {
        e.preventDefault();
        e.stopPropagation();
        if (activeSessionId && sessions.length > 1) {
          removeSession(activeSessionId);
        }
      } else if (e.key === "Tab") {
        e.preventDefault();
        e.stopPropagation();
        if (sessions.length < 2) return;
        const idx = sessions.findIndex((s) => s.id === activeSessionId);
        const next = e.shiftKey
          ? (idx - 1 + sessions.length) % sessions.length
          : (idx + 1) % sessions.length;
        selectSession(sessions[next].id);
      }
    };
    window.addEventListener("keydown", handleKeyDown, true);
    return () => window.removeEventListener("keydown", handleKeyDown, true);
  }, [sessions, activeSessionId, addSession, selectSession, removeSession]);

  return (
    <div className="floating-panel">
      <DragBar />
      <SessionTabBar
        sessions={sessions}
        activeSessionId={activeSessionId}
        onSelect={selectSession}
        onAdd={() => addSession()}
        onClose={removeSession}
      />
      <div className="terminal-area">
        <TerminalView
          sessionId={activeSessionId}
          workingDirectory={activeSession?.workingDirectory}
        />
      </div>
    </div>
  );
}

export default App;
