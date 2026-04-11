import { useRef } from "react";
import { getCurrentWindow } from "@tauri-apps/api/window";

interface DragBarProps {
  onSettingsClick?: () => void;
}

export function DragBar({ onSettingsClick }: DragBarProps) {
  const isDragging = useRef(false);

  const handleMouseDown = (e: React.MouseEvent) => {
    if (e.button !== 0) return;
    // Don't drag if clicking on a button
    if ((e.target as HTMLElement).closest("button")) return;
    isDragging.current = true;
    e.preventDefault();
    getCurrentWindow().startDragging();
  };

  return (
    <div className="drag-bar" onMouseDown={handleMouseDown}>
      <div className="drag-handle" />
      {onSettingsClick && (
        <button
          className="settings-btn"
          onClick={(e) => {
            e.stopPropagation();
            onSettingsClick();
          }}
          title="Settings"
        >
          &#x22EE;
        </button>
      )}
    </div>
  );
}
