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

  const positionBelowNotch = useCallback(async () => {
    const monitor = await currentMonitor();
    if (!monitor) return;
    const screenWidth = monitor.size.width / monitor.scaleFactor;
    const x = Math.round((screenWidth - DEFAULT_WIDTH) / 2);
    await appWindow.setSize(new LogicalSize(DEFAULT_WIDTH, DEFAULT_HEIGHT));
    await appWindow.setPosition(new LogicalPosition(x, TOP_OFFSET));
  }, [appWindow]);

  useEffect(() => {
    const update = async () => {
      if (isExpanded) {
        console.log("[FloatingPanel] showing main window");
        await positionBelowNotch();
        await appWindow.show();
        await appWindow.setFocus();
      } else {
        console.log("[FloatingPanel] hiding main window");
        await appWindow.hide();
      }
    };
    update();
  }, [isExpanded, positionBelowNotch, appWindow]);

  // Start hidden
  useEffect(() => {
    appWindow.hide();
  }, []);

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
