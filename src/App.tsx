import { useEffect, useMemo, useState, useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";
import { TerminalView } from "./components/TerminalView";
import { SessionTabBar } from "./components/SessionTabBar";
import { useSessionStore } from "./hooks/useSessionStore";
import { useAttention } from "./hooks/useAttention";
import { listen } from "@tauri-apps/api/event";
import { DragBar } from "./components/DragBar";
import { HotkeyCaptureModal } from "./components/HotkeyCaptureModal";
import { ThemesModal } from "./components/ThemesModal";
import { useThemes } from "./hooks/useThemes";
import { BUILTIN_THEMES, applyThemeToCSS, getTerminalTheme } from "./lib/themes";
import "./App.css";

function App() {
  const {
    sessions,
    activeSessionId,
    addSession,
    selectSession,
    removeSession,
    renameSession,
    refresh,
  } = useSessionStore();

  const sessionExists = useCallback(
    (id: string) => sessions.some((s) => s.id === id),
    [sessions]
  );

  useAttention(activeSessionId, selectSession, sessionExists);

  const [currentTheme, setCurrentTheme] = useState("vs-dark");
  const [fontSize, setFontSize] = useState(11);
  const [pillShape, setPillShape] = useState(false);
  const [hotkey, setHotkey] = useState("CmdOrCtrl+`");
  const [hotkeyModalOpen, setHotkeyModalOpen] = useState(false);
  const [panelOpacity, setPanelOpacity] = useState(1);
  const [panelFadeContent, setPanelFadeContent] = useState(false);
  const [themesModalOpen, setThemesModalOpen] = useState(false);

  const activeSession = useMemo(
    () => sessions.find((s) => s.id === activeSessionId),
    [sessions, activeSessionId]
  );

  const themes = useThemes();
  const allThemes = themes.all;

  const terminalTheme = useMemo(
    () => getTerminalTheme(allThemes[currentTheme] || BUILTIN_THEMES["vs-dark"]),
    [allThemes, currentTheme]
  );

  // Apply chrome theme on change
  useEffect(() => {
    const theme = allThemes[currentTheme] || BUILTIN_THEMES["vs-dark"];
    applyThemeToCSS(theme, panelOpacity, panelFadeContent);
  }, [allThemes, currentTheme, panelOpacity, panelFadeContent]);

  // Load saved theme from localStorage
  useEffect(() => {
    const saved = localStorage.getItem("shelly-theme");
    if (saved && BUILTIN_THEMES[saved]) setCurrentTheme(saved);
    const savedSize = localStorage.getItem("shelly-font-size");
    if (savedSize) setFontSize(parseInt(savedSize, 10));
    invoke<string>("get_hotkey").then(setHotkey).catch(() => {});
    invoke<{
      theme?: string;
      panelOpacity?: number;
      panelFadeContent?: boolean;
    }>("get_settings").then((s) => {
      if (typeof s.theme === "string") setCurrentTheme(s.theme);
      if (typeof s.panelOpacity === "number") setPanelOpacity(s.panelOpacity);
      if (typeof s.panelFadeContent === "boolean") setPanelFadeContent(s.panelFadeContent);
    }).catch(() => {});
  }, []);

  const handleThemeChange = useCallback((themeId: string) => {
    setCurrentTheme(themeId);
    localStorage.setItem("shelly-theme", themeId);
  }, []);

  const handleFontSizeChange = useCallback((size: number) => {
    setFontSize(size);
    localStorage.setItem("shelly-font-size", String(size));
  }, []);

  // Sync border-radius with window animation
  useEffect(() => {
    const unlisten = listen<boolean>("panel-animating", (e) => {
      setPillShape(e.payload);
    });
    return () => { unlisten.then((f) => f()); };
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
    <div className={`floating-panel ${pillShape ? "panel-pill" : ""}`}>
      <DragBar />
      <SessionTabBar
        sessions={sessions}
        activeSessionId={activeSessionId}
        onSelect={selectSession}
        onAdd={() => addSession()}
        onClose={removeSession}
        onRename={renameSession}
        onRefresh={refresh}
        currentTheme={currentTheme}
        currentFontSize={fontSize}
        onThemeChange={handleThemeChange}
        onFontSizeChange={handleFontSizeChange}
        hotkey={hotkey}
        onOpenHotkeyModal={() => setHotkeyModalOpen(true)}
        onOpenThemesModal={() => setThemesModalOpen(true)}
      />
      <div className="terminal-area">
        <TerminalView
          sessionId={activeSessionId}
          workingDirectory={activeSession?.workingDirectory}
          theme={terminalTheme}
          fontSize={fontSize}
        />
      </div>
      {hotkeyModalOpen && (
        <HotkeyCaptureModal
          initial={hotkey}
          onSaved={setHotkey}
          onClose={() => setHotkeyModalOpen(false)}
        />
      )}
      {themesModalOpen && (
        <ThemesModal
          currentThemeId={currentTheme}
          currentOpacity={panelOpacity}
          currentFadeContent={panelFadeContent}
          onSaved={async (id, op, fade) => {
            setCurrentTheme(id);
            setPanelOpacity(op);
            setPanelFadeContent(fade);
            localStorage.setItem("shelly-theme", id);
            try {
              const current: any = await invoke("get_settings");
              await invoke("save_app_settings", {
                newSettings: { ...current, theme: id, panelOpacity: op, panelFadeContent: fade },
              });
            } catch {}
          }}
          onClose={() => setThemesModalOpen(false)}
        />
      )}
    </div>
  );
}

export default App;
