import { useEffect, useMemo, useState, useCallback } from "react";
import { TerminalView } from "./components/TerminalView";
import { SessionTabBar } from "./components/SessionTabBar";
import { FloatingPanel } from "./components/FloatingPanel";
import { useSessionStore } from "./hooks/useSessionStore";
import { listen } from "@tauri-apps/api/event";
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

  const [isExpanded, setIsExpanded] = useState(true);

  const activeSession = useMemo(
    () => sessions.find((s) => s.id === activeSessionId),
    [sessions, activeSessionId]
  );

  const togglePanel = useCallback(() => {
    setIsExpanded((prev) => {
      const next = !prev;
      const win = getCurrentWindow();
      if (next) {
        win.show();
        win.setFocus();
      } else {
        win.hide();
      }
      return next;
    });
  }, []);

  const handleExpand = useCallback((_pin: boolean) => {
    setIsExpanded(true);
  }, []);

  // Listen for tray/hotkey events
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

  // Keyboard shortcuts — use capture phase to intercept before xterm
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
    // Use capture phase to intercept before xterm gets the event
    window.addEventListener("keydown", handleKeyDown, true);
    return () => window.removeEventListener("keydown", handleKeyDown, true);
  }, [sessions, activeSessionId, addSession, selectSession, removeSession]);

  return (
    <FloatingPanel
      isExpanded={isExpanded}
      onExpand={handleExpand}
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
