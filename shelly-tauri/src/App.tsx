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

  const expandPanel = useCallback(() => {
    console.log("[App] expandPanel");
    setIsExpanded(true);
  }, []);

  const collapsePanel = useCallback(() => {
    console.log("[App] collapsePanel");
    setIsExpanded(false);
  }, []);

  const togglePanel = useCallback(() => {
    setIsExpanded((prev) => {
      console.log(`[App] togglePanel: ${prev} -> ${!prev}`);
      return !prev;
    });
  }, []);

  // Listen for tray/hotkey events
  useEffect(() => {
    console.log("[App] setting up tray event listeners");
    const unlistenToggle = listen("tray-toggle-panel", () => {
      console.log("[App] received tray-toggle-panel event");
      togglePanel();
    });
    const unlistenNewSession = listen("tray-new-session", () => {
      console.log("[App] received tray-new-session event");
      addSession();
    });
    return () => {
      unlistenToggle.then((f) => f());
      unlistenNewSession.then((f) => f());
    };
  }, [addSession, togglePanel]);

  // Auto-collapse on blur when expanded
  useEffect(() => {
    let blurTimer: number | null = null;
    const handleBlur = () => {
      if (!isExpanded) return;
      // Delay to avoid collapse during drag or resize
      blurTimer = window.setTimeout(() => {
        console.log("[App] window blur -> collapsing");
        collapsePanel();
      }, 300);
    };
    const handleFocus = () => {
      if (blurTimer) {
        clearTimeout(blurTimer);
        blurTimer = null;
      }
    };
    window.addEventListener("blur", handleBlur);
    window.addEventListener("focus", handleFocus);
    return () => {
      window.removeEventListener("blur", handleBlur);
      window.removeEventListener("focus", handleFocus);
      if (blurTimer) clearTimeout(blurTimer);
    };
  }, [isExpanded, collapsePanel]);

  // Keyboard shortcuts — use capture phase to intercept before xterm
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const mod = e.ctrlKey || e.metaKey;
      if (!mod) return;

      if (e.key === "t") {
        console.log("[App] Ctrl+T: new session");
        e.preventDefault();
        e.stopPropagation();
        addSession();
      } else if (e.key === "w") {
        console.log("[App] Ctrl+W: close session");
        e.preventDefault();
        e.stopPropagation();
        if (activeSessionId && sessions.length > 1) {
          removeSession(activeSessionId);
        }
      } else if (e.key === "Tab") {
        console.log("[App] Ctrl+Tab: cycle session");
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
    <FloatingPanel
      sessions={sessions}
      isExpanded={isExpanded}
      onExpand={expandPanel}
      onCollapse={collapsePanel}
      onSettingsClick={() => {
        console.log("[App] settings clicked (TODO)");
      }}
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
