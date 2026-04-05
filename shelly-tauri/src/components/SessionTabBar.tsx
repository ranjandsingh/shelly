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
];

const STATUS_ICON: Record<string, React.ReactNode> = {
  Idle: <span className="st-dot" />,
  Working: <span className="st-spin" />,
  WaitingForInput: <span className="st-tri" />,
  TaskCompleted: <span className="st-chk">&#x2713;</span>,
  Interrupted: <span className="st-dot red" />,
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
  const [showHints, setShowHints] = useState(true);
  const [hint, setHint] = useState(() => HINTS[Math.floor(Math.random() * HINTS.length)]);
  const [overflow, setOverflow] = useState(false);
  const hintIdx = useRef(Math.floor(Math.random() * HINTS.length));
  const menuRef = useRef<HTMLDivElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);

  // --- Overflow detection ---
  const checkOverflow = useCallback(() => {
    const el = scrollRef.current;
    if (!el) return;
    setOverflow(el.scrollWidth > el.clientWidth + 4);
  }, []);

  useEffect(() => {
    checkOverflow();
    const ro = new ResizeObserver(checkOverflow);
    if (scrollRef.current) ro.observe(scrollRef.current);
    return () => ro.disconnect();
  }, [sessions.length, checkOverflow]);

  // --- Rotate hint text every 12s (simple interval, no fade) ---
  useEffect(() => {
    if (!showHints) return;
    const id = setInterval(() => {
      hintIdx.current = (hintIdx.current + 1) % HINTS.length;
      setHint(HINTS[hintIdx.current]);
    }, 12000);
    return () => clearInterval(id);
  }, [showHints]);

  // --- Close menu on outside click ---
  useEffect(() => {
    if (!showMenu) return;
    const h = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setShowMenu(false);
    };
    document.addEventListener("mousedown", h);
    return () => document.removeEventListener("mousedown", h);
  }, [showMenu]);

  // --- Scroll ---
  const scrollBy = (dx: number) => {
    scrollRef.current?.scrollBy({ left: dx, behavior: "smooth" });
  };

  return (
    <div className="tabbar">
      {/* Left: icon */}
      <img src="/icon.png" alt="" className="tabbar-icon" />

      {/* Left scroll arrow */}
      {overflow && (
        <button className="tabbar-arrow" onClick={() => scrollBy(-120)}>&#x276E;</button>
      )}

      {/* Tabs (scrollable) */}
      <div
        className="tabbar-scroll"
        ref={scrollRef}
        onWheel={(e) => { scrollRef.current!.scrollLeft += e.deltaY > 0 ? 60 : -60; }}
      >
        {sessions.map((s) => (
          <div
            key={s.id}
            className={`tabbar-tab ${s.id === activeSessionId ? "active" : ""}`}
            onClick={() => onSelect(s.id)}
          >
            {STATUS_ICON[s.status] || STATUS_ICON.Idle}
            <span className="tabbar-tab-name">{s.projectName}</span>
            {sessions.length > 1 && (
              <button className="tabbar-tab-x" onClick={(e) => { e.stopPropagation(); onClose(s.id); }}>&#x2715;</button>
            )}
          </div>
        ))}
        {/* + button inside scroll area so it's always after the last tab */}
        <button className="tabbar-btn plus" onClick={onAdd} title="New session (Ctrl+T)">+</button>
      </div>

      {/* Right scroll arrow */}
      {overflow && (
        <button className="tabbar-arrow" onClick={() => scrollBy(120)}>&#x276F;</button>
      )}

      {/* Hint — visible unless user disabled or tabs overflow */}
      {showHints && !overflow && (
        <span className="tabbar-hint show">Tip: {hint}</span>
      )}

      {/* Right buttons */}
      <button className="tabbar-btn" onClick={async () => { try { await invoke("pick_folder"); onRefresh(); } catch {} }} title="Open folder">&#x1F4C2;</button>
      <button className={`tabbar-btn ${isPinned ? "on" : ""}`} onClick={async () => { const n = !isPinned; setIsPinned(n); await invoke("set_pinned", { pinned: n }); }} title={isPinned ? "Unpin" : "Pin"}>&#x1F4CC;</button>
      <div className="tabbar-menu-wrap" ref={menuRef}>
        <button className="tabbar-btn" onClick={() => setShowMenu(!showMenu)} title="Menu">&#x22EE;</button>
        {showMenu && (
          <SettingsMenu
            onClose={() => setShowMenu(false)}
            onThemeChange={onThemeChange}
            onFontSizeChange={onFontSizeChange}
            currentTheme={currentTheme}
            currentFontSize={currentFontSize}
            showHints={showHints}
            onToggleHints={() => setShowHints(!showHints)}
          />
        )}
      </div>
    </div>
  );
}

