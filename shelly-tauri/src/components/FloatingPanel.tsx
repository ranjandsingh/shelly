import { useEffect, useCallback } from "react";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { currentMonitor } from "@tauri-apps/api/window";
import { LogicalPosition, LogicalSize } from "@tauri-apps/api/dpi";
import { motion } from "framer-motion";
import { DragBar } from "./DragBar";

interface FloatingPanelProps {
  isExpanded: boolean;
  onSettingsClick?: () => void;
  children: React.ReactNode;
}

const DEFAULT_WIDTH = 720;
const DEFAULT_HEIGHT = 400;
const TOP_OFFSET = 30; // below the notch

export function FloatingPanel({
  isExpanded,
  onSettingsClick,
  children,
}: FloatingPanelProps) {
  const appWindow = getCurrentWindow();

  const showPanel = useCallback(async () => {
    console.log("[FloatingPanel] showPanel called");
    try {
      const monitor = await currentMonitor();
      if (monitor) {
        const screenWidth = monitor.size.width / monitor.scaleFactor;
        const x = Math.round((screenWidth - DEFAULT_WIDTH) / 2);
        console.log(`[FloatingPanel] positioning at x=${x}, y=${TOP_OFFSET}, size=${DEFAULT_WIDTH}x${DEFAULT_HEIGHT}`);
        await appWindow.setSize(new LogicalSize(DEFAULT_WIDTH, DEFAULT_HEIGHT));
        await appWindow.setPosition(new LogicalPosition(x, TOP_OFFSET));
      }
      await appWindow.show();
      await appWindow.setFocus();
      console.log("[FloatingPanel] window shown and focused");
    } catch (e) {
      console.error("[FloatingPanel] showPanel error:", e);
    }
  }, [appWindow]);

  const hidePanel = useCallback(async () => {
    console.log("[FloatingPanel] hidePanel called");
    try {
      await appWindow.hide();
    } catch (e) {
      console.error("[FloatingPanel] hidePanel error:", e);
    }
  }, [appWindow]);

  useEffect(() => {
    if (isExpanded) {
      showPanel();
    } else {
      hidePanel();
    }
  }, [isExpanded, showPanel, hidePanel]);

  const handleResizeMouseDown = useCallback(
    async (e: React.MouseEvent) => {
      e.preventDefault();
      await (appWindow as any).startResizeDragging("SouthEast");
    },
    [appWindow]
  );

  if (!isExpanded) return null;

  return (
    <motion.div
      className="floating-panel"
      initial={{ opacity: 0, scale: 0.96, y: -10 }}
      animate={{ opacity: 1, scale: 1, y: 0 }}
      transition={{
        type: "spring",
        stiffness: 300,
        damping: 25,
        mass: 0.8,
      }}
    >
      <DragBar onSettingsClick={onSettingsClick} />
      {children}
      <div className="resize-grip" onMouseDown={handleResizeMouseDown} />
    </motion.div>
  );
}
