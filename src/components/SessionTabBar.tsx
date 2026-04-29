import { useState, useEffect, useRef, useCallback, useMemo } from "react";
import { invoke } from "@tauri-apps/api/core";
import { TerminalSession } from "../hooks/useSessionStore";
import { SettingsMenu } from "./SettingsMenu";
import { RecentFoldersDropdown } from "./RecentFoldersDropdown";
import { resolveTabColor, NAMED_COLORS, PALETTE_ORDER } from "../lib/tabColor";
import { getPathColors, setPathColor } from "../lib/ipc";

interface SessionTabBarProps {
  sessions: TerminalSession[];
  activeSessionId: string | null;
  onSelect: (id: string) => void;
  onAdd: () => void;
  onClose: (id: string) => void;
  onRename: (id: string, name: string) => void;
  onRefresh: () => void;
  hotkey: string;
  onOpenHotkeyModal: () => void;
  onOpenThemesModal: () => void;
}

const IS_MAC = typeof navigator !== "undefined" && navigator.platform.toLowerCase().includes("mac");

function prettyHotkey(accel: string): string {
  const parts = accel.split("+").map((p) => p.trim());
  const out: string[] = [];
  for (const p of parts) {
    const lower = p.toLowerCase();
    if (lower === "cmdorctrl") out.push(IS_MAC ? "\u2318" : "Ctrl");
    else if (lower === "ctrl" || lower === "control") out.push(IS_MAC ? "\u2303" : "Ctrl");
    else if (lower === "alt" || lower === "option") out.push(IS_MAC ? "\u2325" : "Alt");
    else if (lower === "shift") out.push(IS_MAC ? "\u21E7" : "Shift");
    else if (lower === "cmd" || lower === "command" || lower === "meta" || lower === "super") out.push(IS_MAC ? "\u2318" : "Win");
    else out.push(p);
  }
  return out.join(IS_MAC ? "" : "+");
}

const BASE_HINTS = [
  "Drop a folder to open it",
  "Right-click tab to rename",
  "Pin to keep panel open",
  "Ctrl+T opens a new session",
  "Ctrl+W closes current tab",
  "Drag the top bar to move",
];

const STATUS_ICON: Record<string, React.ReactNode> = {
  Idle: null,
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
  hotkey,
  onOpenHotkeyModal,
  onOpenThemesModal,
}: SessionTabBarProps) {
  const hints = useMemo(
    () => [`${prettyHotkey(hotkey)} to toggle panel`, ...BASE_HINTS],
    [hotkey]
  );
  const [isPinned, setIsPinned] = useState(false);
  const [showMenu, setShowMenu] = useState(false);

  // Sync pinned state from Rust on mount and when panel becomes visible
  useEffect(() => {
    invoke<boolean>("get_pinned").then(setIsPinned);
    let unlisten: (() => void) | null = null;
    import("@tauri-apps/api/event").then(({ listen }) => {
      listen<boolean>("panel-visibility", (e) => {
        setShowMenu(false);
        setContextMenu(null);
        if (e.payload) invoke<boolean>("get_pinned").then(setIsPinned);
      }).then((fn) => { unlisten = fn; });
    });
    return () => { unlisten?.(); };
  }, []);
  const [showHints, setShowHints] = useState(true);
  const [hintIdxState, setHintIdxState] = useState(() => Math.floor(Math.random() * hints.length));
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(false);
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; sessionId: string } | null>(null);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const hint = hints[hintIdxState % hints.length];
  const [recentOpen, setRecentOpen] = useState(false);
  const [pathColors, setPathColors] = useState<Record<string, string>>({});
  const recentBtnRef = useRef<HTMLDivElement>(null);
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

  // --- Scroll active tab into view when activeSessionId changes ---
  useEffect(() => {
    const scroll = scrollRef.current;
    const activeEl = scroll?.querySelector('.tabbar-tab.active') as HTMLElement | null;
    if (!activeEl || !scroll) return;
    const tabLeft = activeEl.offsetLeft;
    const tabRight = activeEl.offsetLeft + activeEl.offsetWidth;
    if (tabLeft < scroll.scrollLeft) {
      scroll.scrollLeft = tabLeft - 8;
    } else if (tabRight > scroll.scrollLeft + scroll.clientWidth) {
      scroll.scrollLeft = tabRight - scroll.clientWidth + 8;
    }
    const id = setTimeout(updateScrollState, 100);
    return () => clearTimeout(id);
  }, [activeSessionId, updateScrollState]);

  // --- Rotate hint text every 12s ---
  useEffect(() => {
    if (!showHints) return;
    const id = setInterval(() => {
      setHintIdxState((i) => (i + 1) % hints.length);
    }, 12000);
    return () => clearInterval(id);
  }, [showHints, hints.length]);

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

  // --- Load path colors + subscribe to updates ---
  useEffect(() => {
    getPathColors().then(setPathColors);
    let un: (() => void) | null = null;
    import("@tauri-apps/api/event").then(({ listen }) => {
      listen("path-colors-updated", async () => {
        setPathColors(await getPathColors());
      }).then(fn => { un = fn; });
    });
    return () => { un?.(); };
  }, []);

  const cwdCounts = useMemo(() => {
    const m = new Map<string, number>();
    for (const s of sessions) {
      const cwd = s.workingDirectory;
      if (!cwd) continue;
      m.set(cwd, (m.get(cwd) ?? 0) + 1);
    }
    return m;
  }, [sessions]);

  const openPaths = useMemo(
    () => new Set(Array.from(cwdCounts.keys())),
    [cwdCounts],
  );

  const colorForPath = useCallback(
    (p: string) => resolveTabColor(p, cwdCounts.get(p) ?? 0, pathColors),
    [cwdCounts, pathColors],
  );

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
            {(() => {
              const color = colorForPath(s.workingDirectory);
              return color ? <span className="tab-color-strip" style={{ background: color }} /> : null;
            })()}
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
          {(() => {
            const sess = sessions.find(s => s.id === contextMenu.sessionId);
            if (!sess) return null;
            const cwd = sess.workingDirectory;
            const currentOverride = pathColors[cwd] ?? "auto";
            const apply = async (color: string) => {
              await setPathColor(cwd, color);
              setContextMenu(null);
            };
            return (
              <>
                <div className="ctx-section-label">Color</div>
                <div className="ctx-color-palette">
                  <button
                    className={`ctx-swatch auto${currentOverride === "auto" ? " active" : ""}`}
                    onClick={() => apply("auto")}
                    title="Auto"
                  >A</button>
                  {PALETTE_ORDER.map(name => (
                    <button
                      key={name}
                      className={`ctx-swatch${currentOverride === name ? " active" : ""}`}
                      style={{ background: NAMED_COLORS[name] }}
                      onClick={() => apply(name)}
                      title={name}
                    />
                  ))}
                  <button
                    className={`ctx-swatch none${currentOverride === "none" ? " active" : ""}`}
                    onClick={() => apply("none")}
                    title="None"
                  >∅</button>
                </div>
              </>
            );
          })()}
          {sessions.length > 1 && (
            <div className="menu-item danger" onClick={() => { onClose(contextMenu.sessionId); setContextMenu(null); }}>
              Close
            </div>
          )}
        </div>
      )}

      {/* Right buttons — always visible, never affected by overflow */}
      <div className="folder-split" ref={recentBtnRef}>
        <button className="tabbar-btn folder-split-main" onClick={async () => { try { await invoke("pick_folder"); onRefresh(); } catch {} }} title="Open folder">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor">
            <path d="M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/>
          </svg>
        </button>
        <span className="folder-split-sep" />
        <button
          className="tabbar-btn folder-split-chev"
          onClick={() => setRecentOpen(v => !v)}
          title="Recent folders"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor">
            <path d="M6 9l6 6 6-6z"/>
          </svg>
        </button>
        <RecentFoldersDropdown
          open={recentOpen}
          onClose={() => setRecentOpen(false)}
          onOpened={onRefresh}
          openPaths={openPaths}
          colorForPath={colorForPath}
        />
      </div>
      <button className={`tabbar-btn pin-btn ${isPinned ? "on" : ""}`} onClick={async () => { const n = !isPinned; setIsPinned(n); await invoke("set_pinned", { pinned: n }); }} title={isPinned ? "Unpin" : "Pin"}>
        <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
          <path d="M9.828.722a.5.5 0 0 1 .354.146l4.95 4.95a.5.5 0 0 1 0 .707c-.48.48-1.072.588-1.503.588-.177 0-.335-.018-.46-.039l-3.134 3.134a5.927 5.927 0 0 1 .16 1.013c.046.702-.032 1.687-.72 2.375a.5.5 0 0 1-.707 0l-2.829-2.828-3.182 3.182c-.195.195-1.219.902-1.414.707-.195-.195.512-1.22.707-1.414l3.182-3.182-2.828-2.829a.5.5 0 0 1 0-.707c.688-.688 1.673-.767 2.375-.72a5.922 5.922 0 0 1 1.013.16l3.134-3.133a2.772 2.772 0 0 1-.04-.461c0-.43.108-1.022.589-1.503a.5.5 0 0 1 .353-.146z"/>
        </svg>
      </button>
      <div className="tabbar-menu-wrap" ref={menuRef}>
        <button className="tabbar-btn" onClick={() => setShowMenu(!showMenu)} title="Menu">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" stroke="none">
            <circle cx="12" cy="5" r="2"/><circle cx="12" cy="12" r="2"/><circle cx="12" cy="19" r="2"/>
          </svg>
        </button>
        {showMenu && (
          <SettingsMenu
            onClose={() => setShowMenu(false)}
            showHints={showHints}
            onToggleHints={() => setShowHints(!showHints)}
            onOpenHotkeyModal={onOpenHotkeyModal}
            onOpenThemesModal={onOpenThemesModal}
          />
        )}
      </div>
    </div>
  );
}
