import { useEffect, useMemo, useRef, useState, useCallback } from "react";
import { invoke } from "@tauri-apps/api/core";
import { TerminalView } from "./components/TerminalView";
import { SessionTabBar } from "./components/SessionTabBar";
import { useSessionStore } from "./hooks/useSessionStore";
import { useAttention } from "./hooks/useAttention";
import { listen } from "@tauri-apps/api/event";
import { DragBar } from "./components/DragBar";
import { HotkeyCaptureModal } from "./components/HotkeyCaptureModal";
import { ThemesModal } from "./components/ThemesModal";
import { UpdateBanner, type UpdateBannerState } from "./components/UpdateBanner";
import { useThemes } from "./hooks/useThemes";
import { BUILTIN_THEMES, applyThemeToCSS, getTerminalTheme } from "./lib/themes";
import { checkForUpdate, downloadUpdate, restartApp } from "./lib/updater";
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
  const [updateBanner, setUpdateBanner] = useState<UpdateBannerState | null>(null);
  const updateInFlight = useRef(false);

  const activeSession = useMemo(
    () => sessions.find((s) => s.id === activeSessionId),
    [sessions, activeSessionId]
  );

  const themes = useThemes();
  const allThemes = themes.all;

  const terminalTheme = useMemo(
    () => getTerminalTheme(allThemes[currentTheme] || BUILTIN_THEMES["vs-dark"], panelOpacity),
    [allThemes, currentTheme, panelOpacity]
  );

  // Apply chrome theme on change
  useEffect(() => {
    const theme = allThemes[currentTheme] || BUILTIN_THEMES["vs-dark"];
    applyThemeToCSS(theme, panelOpacity, panelFadeContent);
  }, [allThemes, currentTheme, panelOpacity, panelFadeContent]);

  const runUpdateCheck = useCallback(async (manual: boolean) => {
    if (updateInFlight.current) return;
    updateInFlight.current = true;
    try {
      if (manual) setUpdateBanner({ kind: "checking" });
      const update = await checkForUpdate();
      if (!update) {
        if (manual) {
          setUpdateBanner({ kind: "uptodate" });
          setTimeout(() => setUpdateBanner((b) => (b?.kind === "uptodate" ? null : b)), 3000);
        } else {
          setUpdateBanner(null);
        }
        return;
      }
      setUpdateBanner({ kind: "downloading", version: update.version });
      await downloadUpdate(update);
      setUpdateBanner({ kind: "ready", version: update.version });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      if (manual) {
        setUpdateBanner({ kind: "error", message });
        setTimeout(() => setUpdateBanner((b) => (b?.kind === "error" ? null : b)), 5000);
      } else {
        console.error("auto update check failed:", err);
        setUpdateBanner(null);
      }
    } finally {
      updateInFlight.current = false;
    }
  }, []);

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
      autoCheckUpdates?: boolean;
    }>("get_settings").then((s) => {
      if (typeof s.theme === "string") setCurrentTheme(s.theme);
      if (typeof s.panelOpacity === "number") setPanelOpacity(s.panelOpacity);
      if (typeof s.panelFadeContent === "boolean") setPanelFadeContent(s.panelFadeContent);
      if (s.autoCheckUpdates !== false) {
        runUpdateCheck(false);
      }
    }).catch(() => {});
  }, [runUpdateCheck]);

  // Tray "Check for Updates" menu item
  useEffect(() => {
    const unlisten = listen("tray-check-updates", () => { runUpdateCheck(true); });
    return () => { unlisten.then((f) => f()); };
  }, [runUpdateCheck]);

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
      {updateBanner && (
        <UpdateBanner
          state={updateBanner}
          onRestart={restartApp}
          onDismiss={() => setUpdateBanner(null)}
        />
      )}
      <SessionTabBar
        sessions={sessions}
        activeSessionId={activeSessionId}
        onSelect={selectSession}
        onAdd={() => addSession()}
        onClose={removeSession}
        onRename={renameSession}
        onRefresh={refresh}
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
          currentFontSize={fontSize}
          onSaved={async (id, op, fade, size) => {
            setCurrentTheme(id);
            setPanelOpacity(op);
            setPanelFadeContent(fade);
            handleFontSizeChange(size);
            localStorage.setItem("shelly-theme", id);
            try {
              const current: any = await invoke("get_settings");
              await invoke("save_app_settings", {
                newSettings: { ...current, theme: id, panelOpacity: op, panelFadeContent: fade, fontSize: size },
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
