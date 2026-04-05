import { getCurrentWindow } from "@tauri-apps/api/window";

export function DragBar() {
  const handleMouseDown = async (e: React.MouseEvent) => {
    // Only drag on left button, and not on child buttons
    if (e.button !== 0) return;
    e.preventDefault();
    await getCurrentWindow().startDragging();
  };

  return (
    <div className="drag-bar" onMouseDown={handleMouseDown}>
      <div className="drag-handle" />
    </div>
  );
}
