import { useEffect, useRef, useState } from "react";
import { listen } from "@tauri-apps/api/event";
import {
  clearRecentFolders,
  getRecentFolders,
  openRecentFolder,
} from "../lib/ipc";

type Props = {
  open: boolean;
  onClose: () => void;
  onOpened?: () => void;
};

function basename(p: string): string {
  const norm = p.replace(/[\\/]+$/, "");
  const i = Math.max(norm.lastIndexOf("/"), norm.lastIndexOf("\\"));
  return i >= 0 ? norm.slice(i + 1) : norm;
}

export function RecentFoldersDropdown({ open, onClose, onOpened }: Props) {
  const [items, setItems] = useState<string[]>([]);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    getRecentFolders().then(setItems);
    let un: (() => void) | null = null;
    listen("recent-folders-updated", async () => {
      setItems(await getRecentFolders());
    }).then(fn => { un = fn; });
    return () => { un?.(); };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const h = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) onClose();
    };
    document.addEventListener("mousedown", h);
    return () => document.removeEventListener("mousedown", h);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="recent-dropdown" ref={rootRef}>
      {items.length === 0 ? (
        <div className="recent-empty">No recent folders</div>
      ) : (
        <>
          <div className="recent-dropdown-list">
            {items.map(path => (
              <button
                key={path}
                className="recent-item"
                title={path}
                onClick={async () => {
                  await openRecentFolder(path);
                  onOpened?.();
                  onClose();
                }}
              >
                <span className="recent-item-name">{basename(path)}</span>
              </button>
            ))}
          </div>
          <div className="recent-dropdown-footer">
            <button
              className="recent-clear"
              onClick={async () => {
                await clearRecentFolders();
                onClose();
              }}
            >
              Clear all
            </button>
          </div>
        </>
      )}
    </div>
  );
}
