import { useEffect, useMemo, useState, useCallback } from "react";
import { TerminalView } from "./components/TerminalView";
import { SessionTabBar } from "./components/SessionTabBar";
import { useSessionStore } from "./hooks/useSessionStore";
import { listen } from "@tauri-apps/api/event";
import { DragBar } from "./components/DragBar";
import { THEMES, applyThemeToCSS, getTerminalTheme } from "./lib/themes";
import "./App.css";

function App() {
  const {
    sessions,
    activeSessionId,
    addSession,
    selectSession,
    removeSession,
    refresh,
  } = useSessionStore();

  const [currentTheme, setCurrentTheme] = useState("vs-dark");
  const [fontSize, setFontSize] = useState(11);

  const activeSession = useMemo(
    () => sessions.find((s) => s.id === activeSessionId),
    [sessions, activeSessionId]
  );

  const terminalTheme = useMemo(
    () => getTerminalTheme(THEMES[currentTheme] || THEMES["vs-dark"]),
    [currentTheme]
  );

  // Apply chrome theme on change
  useEffect(() => {
    const theme = THEMES[currentTheme] || THEMES["vs-dark"];
    applyThemeToCSS(theme);
  }, [currentTheme]);

  // Load saved theme from localStorage
  useEffect(() => {
    const saved = localStorage.getItem("shelly-theme");
    if (saved && THEMES[saved]) setCurrentTheme(saved);
    const savedSize = localStorage.getItem("shelly-font-size");
    if (savedSize) setFontSize(parseInt(savedSize, 10));
  }, []);

  const handleThemeChange = useCallback((themeId: string) => {
    setCurrentTheme(themeId);
    localStorage.setItem("shelly-theme", themeId);
  }, []);

  const handleFontSizeChange = useCallback((size: number) => {
    setFontSize(size);
    localStorage.setItem("shelly-font-size", String(size));
  }, []);

  // Listen for session refresh from Rust
  useEffect(() => {
    const unlisten = listen("sessions-force-refresh", () => { refresh(); });
    return () => { unlisten.then((f) => f()); };
  }, [refresh]);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const mod = e.ctrlKey || e.metaKey;
      if (!mod) return;
      if (e.key === "t") {
        e.preventDefault(); e.stopPropagation(); addSession();
      } else if (e.key === "w") {
        e.preventDefault(); e.stopPropagation();
        if (activeSessionId && sessions.length > 1) removeSession(activeSessionId);
      } else if (e.key === "Tab") {
        e.preventDefault(); e.stopPropagation();
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
        currentTheme={currentTheme}
        currentFontSize={fontSize}
        onThemeChange={handleThemeChange}
        onFontSizeChange={handleFontSizeChange}
      />
      <div className="terminal-area">
        <TerminalView
          sessionId={activeSessionId}
          workingDirectory={activeSession?.workingDirectory}
          theme={terminalTheme}
          fontSize={fontSize}
        />
      </div>
    </div>
  );
}

export default App;
