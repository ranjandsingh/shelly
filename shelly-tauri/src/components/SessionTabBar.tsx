import { useState, useEffect, useRef, useCallback } from "react";
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
  const [tabsOverflow, setTabsOverflow] = useState(false);
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(false);
  const hintIndex = useRef(Math.floor(Math.random() * HINTS.length));
  const menuRef = useRef<HTMLDivElement>(null);
  const tabListRef = useRef<HTMLDivElement>(null);

  // Check if tabs overflow
  const checkOverflow = useCallback(() => {
    const el = tabListRef.current;
    if (!el) return;
    const overflows = el.scrollWidth > el.clientWidth + 2;
    setTabsOverflow(overflows);
    setCanScrollLeft(el.scrollLeft > 0);
    setCanScrollRight(el.scrollLeft + el.clientWidth < el.scrollWidth - 2);
  }, []);

  // Re-check overflow on session changes and resize
  useEffect(() => {
    checkOverflow();
    const observer = new ResizeObserver(checkOverflow);
    if (tabListRef.current) observer.observe(tabListRef.current);
    return () => observer.disconnect();
  }, [sessions, checkOverflow]);

  // Scroll tabs with mouse wheel
  const handleWheel = useCallback((e: React.WheelEvent) => {
    const el = tabListRef.current;
    if (!el) return;
    el.scrollLeft += e.deltaY > 0 ? 80 : -80;
    checkOverflow();
  }, [checkOverflow]);

  const scrollLeft = () => {
    const el = tabListRef.current;
    if (el) { el.scrollLeft -= 120; checkOverflow(); }
  };

  const scrollRight = () => {
    const el = tabListRef.current;
    if (el) { el.scrollLeft += 120; checkOverflow(); }
  };

  // Rotating hints — only when tabs don't overflow
  useEffect(() => {
    const showHint = () => {
      if (tabsOverflow) { setHintVisible(false); return; }
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
  }, [tabsOverflow]);

  // Close menu on outside click
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

      {/* Scroll left arrow */}
      {canScrollLeft && (
        <button className="tab-scroll-btn" onClick={scrollLeft} title="Scroll tabs left">
          &#x276E;
        </button>
      )}

      {/* Scrollable tabs */}
      <div
        className="tab-list"
        ref={tabListRef}
        onWheel={handleWheel}
        onScroll={checkOverflow}
      >
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
        <button className="tab-bar-btn tab-add-btn" onClick={onAdd} title="New session">+</button>
      </div>

      {/* Scroll right arrow */}
      {canScrollRight && (
        <button className="tab-scroll-btn" onClick={scrollRight} title="Scroll tabs right">
          &#x276F;
        </button>
      )}

      {/* Hint text — hidden when tabs overflow */}
      {!tabsOverflow && (
        <div className={`hint-text ${hintVisible ? "visible" : ""}`}>{hint}</div>
      )}

      {/* Right-side buttons */}
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
