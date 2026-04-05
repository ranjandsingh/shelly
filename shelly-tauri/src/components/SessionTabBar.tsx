import { useState, useEffect, useRef } from "react";
import { invoke } from "@tauri-apps/api/core";
import { TerminalSession } from "../hooks/useSessionStore";
import { SettingsMenu } from "./SettingsMenu";

interface SessionTabBarProps {
  sessions: TerminalSession[];
  activeSessionId: string | null;
  onSelect: (id: string) => void;
  onAdd: () => void;
  onClose: (id: string) => void;
  onRefresh: () => void;
  currentTheme: string;
  currentFontSize: number;
  onThemeChange: (themeId: string) => void;
  onFontSizeChange: (size: number) => void;
}

const HINTS = [
  "Ctrl+` to toggle panel",
  "Drop a folder to open it",
  "Right-click tab to rename",
  "Pin to keep panel open",
  "Ctrl+T opens a new session",
  "Ctrl+W closes current tab",
  "Drag the top bar to move",
  "Customize hotkey in Settings",
  "Auto-expands when Claude asks",
];

const STATUS_ICONS: Record<string, React.ReactNode> = {
  Idle: <span className="status-dot idle" />,
  Working: <span className="status-spinner" />,
  WaitingForInput: <span className="status-triangle" />,
  TaskCompleted: <span className="status-check">&#x2713;</span>,
  Interrupted: <span className="status-dot interrupted" />,
};

export function SessionTabBar({
  sessions,
  activeSessionId,
  onSelect,
  onAdd,
  onClose,
  onRefresh,
  currentTheme,
  currentFontSize,
  onThemeChange,
  onFontSizeChange,
}: SessionTabBarProps) {
  const [isPinned, setIsPinned] = useState(false);
  const [showMenu, setShowMenu] = useState(false);
  const [hint, setHint] = useState("");
  const [hintVisible, setHintVisible] = useState(false);
  const hintIndex = useRef(Math.floor(Math.random() * HINTS.length));
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const showHint = () => {
      setHint(`Tip: ${HINTS[hintIndex.current]}`);
      setHintVisible(true);
      setTimeout(() => {
        setHintVisible(false);
        hintIndex.current = (hintIndex.current + 1) % HINTS.length;
      }, 5000);
    };
    showHint();
    const interval = setInterval(showHint, 15000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    if (!showMenu) return;
    const handler = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setShowMenu(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [showMenu]);

  const handlePin = async () => {
    const next = !isPinned;
    setIsPinned(next);
    await invoke("set_pinned", { pinned: next });
  };

  const handleOpenFolder = async () => {
    try {
      await invoke("pick_folder");
      // Refresh to pick up the new session created by Rust
      onRefresh();
    } catch (e) {
      console.error("[SessionTabBar] pick_folder error:", e);
    }
  };

  return (
    <div className="session-tab-bar">
      <div className="tab-bar-icon">
        <img src="/icon.png" alt="" className="app-icon-img" />
      </div>

      <div className="tab-list">
        {sessions.map((s) => (
          <div
            key={s.id}
            className={`tab ${s.id === activeSessionId ? "active" : ""}`}
            onClick={() => onSelect(s.id)}
          >
            {STATUS_ICONS[s.status] || STATUS_ICONS.Idle}
            <span className="tab-name">{s.projectName}</span>
            {sessions.length > 1 && (
              <button
                className="tab-close"
                onClick={(e) => {
                  e.stopPropagation();
                  onClose(s.id);
                }}
                title="Close tab"
              >
                &#x2715;
              </button>
            )}
          </div>
        ))}
        <button className="tab-bar-btn" onClick={onAdd} title="New session">+</button>
      </div>

      <div className={`hint-text ${hintVisible ? "visible" : ""}`}>{hint}</div>

      <button className="tab-bar-btn" onClick={handleOpenFolder} title="Open folder">
        &#x1F4C2;
      </button>
      <button
        className={`tab-bar-btn ${isPinned ? "pinned" : ""}`}
        onClick={handlePin}
        title={isPinned ? "Unpin" : "Pin panel"}
      >
        &#x1F4CC;
      </button>
      <div className="menu-container" ref={menuRef}>
        <button className="tab-bar-btn" onClick={() => setShowMenu(!showMenu)} title="Menu">
          &#x22EE;
        </button>
        {showMenu && (
          <SettingsMenu
            onClose={() => setShowMenu(false)}
            onThemeChange={onThemeChange}
            onFontSizeChange={onFontSizeChange}
            currentTheme={currentTheme}
            currentFontSize={currentFontSize}
          />
        )}
      </div>
    </div>
  );
}
