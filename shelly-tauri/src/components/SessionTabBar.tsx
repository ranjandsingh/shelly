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
  onRename: (id: string, name: string) => void;
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
  Exited: <span className="st-dot red" />,
};

export function SessionTabBar({
  sessions,
  activeSessionId,
  onSelect,
  onAdd,
  onClose,
  onRename,
  onRefresh,
  currentTheme,
  currentFontSize,
  onThemeChange,
  onFontSizeChange,
}: SessionTabBarProps) {
  const [isPinned, setIsPinned] = useState(false);
  const [showMenu, setShowMenu] = useState(false);

  // Sync pinned state from Rust on mount and when panel becomes visible
  useEffect(() => {
    invoke<boolean>("get_pinned").then(setIsPinned);
    let unlisten: (() => void) | null = null;
    import("@tauri-apps/api/event").then(({ listen }) => {
      listen<boolean>("panel-visibility", (e) => {
        if (e.payload) invoke<boolean>("get_pinned").then(setIsPinned);
      }).then((fn) => { unlisten = fn; });
    });
    return () => { unlisten?.(); };
  }, []);
  const [showHints, setShowHints] = useState(true);
  const [hint, setHint] = useState(() => HINTS[Math.floor(Math.random() * HINTS.length)]);
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(false);
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; sessionId: string } | null>(null);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const hintIdx = useRef(Math.floor(Math.random() * HINTS.length));
  const menuRef = useRef<HTMLDivElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const ctxMenuRef = useRef<HTMLDivElement>(null);
  const renameInputRef = useRef<HTMLInputElement>(null);

  // --- Check scroll state (not overflow — just whether arrows are needed) ---
  const updateScrollState = useCallback(() => {
    const el = scrollRef.current;
    if (!el) return;
    setCanScrollLeft(el.scrollLeft > 1);
    setCanScrollRight(el.scrollLeft + el.clientWidth < el.scrollWidth - 1);
  }, []);

  useEffect(() => {
    updateScrollState();
    // Re-check when sessions change
  }, [sessions.length, updateScrollState]);

  // --- Rotate hint text every 12s ---
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

  // --- Close context menu on outside click ---
  useEffect(() => {
    if (!contextMenu) return;
    const h = (e: MouseEvent) => {
      if (ctxMenuRef.current && !ctxMenuRef.current.contains(e.target as Node)) setContextMenu(null);
    };
    document.addEventListener("mousedown", h);
    return () => document.removeEventListener("mousedown", h);
  }, [contextMenu]);

  // --- Focus rename input when entering rename mode ---
  useEffect(() => {
    if (renamingId && renameInputRef.current) {
      renameInputRef.current.focus();
      renameInputRef.current.select();
    }
  }, [renamingId]);

  const handleContextMenu = (e: React.MouseEvent, sessionId: string) => {
    e.preventDefault();
    setContextMenu({ x: e.clientX, y: e.clientY, sessionId });
  };

  const startRename = (sessionId: string) => {
    const session = sessions.find((s) => s.id === sessionId);
    setRenameValue(session?.projectName || "");
    setRenamingId(sessionId);
    setContextMenu(null);
  };

  const commitRename = () => {
    if (renamingId && renameValue.trim()) {
      onRename(renamingId, renameValue.trim());
    }
    setRenamingId(null);
  };

  const scrollBy = (dx: number) => {
    scrollRef.current?.scrollBy({ left: dx, behavior: "smooth" });
    setTimeout(updateScrollState, 300);
  };

  return (
    <div className="tabbar">
      {/* Left: icon */}
      <img src="/icon.png" alt="" className="tabbar-icon" />

      {/* Left scroll arrow */}
      {canScrollLeft && (
        <button className="tabbar-arrow" onClick={() => scrollBy(-120)}>&#x276E;</button>
      )}

      {/* Tabs scroll area — takes all available space */}
      <div
        className="tabbar-scroll"
        ref={scrollRef}
        onWheel={(e) => {
          scrollRef.current!.scrollLeft += e.deltaY > 0 ? 60 : -60;
          updateScrollState();
        }}
        onScroll={updateScrollState}
      >
        {sessions.map((s) => (
          <div
            key={s.id}
            className={`tabbar-tab ${s.id === activeSessionId ? "active" : ""}`}
            onClick={() => onSelect(s.id)}
            onContextMenu={(e) => handleContextMenu(e, s.id)}
          >
            {STATUS_ICON[s.status] || STATUS_ICON.Idle}
            {renamingId === s.id ? (
              <input
                ref={renameInputRef}
                className="tabbar-tab-rename"
                value={renameValue}
                onChange={(e) => setRenameValue(e.target.value)}
                onBlur={commitRename}
                onKeyDown={(e) => {
                  if (e.key === "Enter") commitRename();
                  if (e.key === "Escape") setRenamingId(null);
                }}
                onClick={(e) => e.stopPropagation()}
              />
            ) : (
              <span className="tabbar-tab-name">{s.projectName}</span>
            )}
            {sessions.length > 1 && (
              <button className="tabbar-tab-x" onClick={(e) => { e.stopPropagation(); onClose(s.id); }}>&#x2715;</button>
            )}
          </div>
        ))}
        <button className="tabbar-btn plus" onClick={onAdd} title="New session (Ctrl+T)">+</button>

        {/* Hint INSIDE the scroll area — it just scrolls away naturally when tabs overflow */}
        {showHints && (
          <span className="tabbar-hint">Tip: {hint}</span>
        )}
      </div>

      {/* Right scroll arrow */}
      {canScrollRight && (
        <button className="tabbar-arrow" onClick={() => scrollBy(120)}>&#x276F;</button>
      )}

      {/* Right-click context menu */}
      {contextMenu && (
        <div
          ref={ctxMenuRef}
          className="tab-context-menu"
          style={{ left: contextMenu.x, top: contextMenu.y }}
        >
          <div className="menu-item" onClick={() => startRename(contextMenu.sessionId)}>
            Rename
          </div>
          {sessions.length > 1 && (
            <div className="menu-item danger" onClick={() => { onClose(contextMenu.sessionId); setContextMenu(null); }}>
              Close
            </div>
          )}
        </div>
      )}

      {/* Right buttons — always visible, never affected by overflow */}
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
