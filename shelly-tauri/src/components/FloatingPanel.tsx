import { useEffect, useCallback } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { currentMonitor } from "@tauri-apps/api/window";
import { LogicalPosition, LogicalSize } from "@tauri-apps/api/dpi";
import { DragBar } from "./DragBar";

interface FloatingPanelProps {
  isExpanded: boolean;
  onExpand: (pin: boolean) => void;
  children: React.ReactNode;
}

const DEFAULT_WIDTH = 720;
const DEFAULT_HEIGHT = 400;

export function FloatingPanel({
  isExpanded,
  onExpand,
  children,
}: FloatingPanelProps) {
  const appWindow = getCurrentWindow();

  const positionCenter = useCallback(
    async (width: number, height: number) => {
      const monitor = await currentMonitor();
      if (!monitor) return;
      const screenWidth = monitor.size.width / monitor.scaleFactor;
      const x = (screenWidth - width) / 2;
      await appWindow.setSize(new LogicalSize(width, height));
      await appWindow.setPosition(new LogicalPosition(x, 0));
    },
    [appWindow]
  );

  // When isExpanded changes, resize and position the window
  useEffect(() => {
    if (isExpanded) {
      positionCenter(DEFAULT_WIDTH, DEFAULT_HEIGHT).then(() => {
        appWindow.show();
        appWindow.setFocus();
      });
    } else {
      appWindow.hide();
    }
  }, [isExpanded, positionCenter, appWindow]);

  // Show window on first mount in expanded state
  useEffect(() => {
    onExpand(true);
  }, []);

  const handleResizeMouseDown = useCallback(
    async (e: React.MouseEvent) => {
      e.preventDefault();
      await (appWindow as any).startResizeDragging("SouthEast");
    },
    [appWindow]
  );

  if (!isExpanded) {
    return null; // Window is hidden when collapsed
  }

  return (
    <AnimatePresence>
      <motion.div
        className="floating-panel"
        initial={{ opacity: 0, scale: 0.93, y: -20 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        exit={{ opacity: 0, scale: 0.9, y: -15 }}
        transition={{
          type: "spring",
          stiffness: 300,
          damping: 25,
          mass: 0.8,
        }}
        onMouseDown={() => {}}
      >
        <DragBar />
        {children}
        <div className="resize-grip" onMouseDown={handleResizeMouseDown} />
      </motion.div>
    </AnimatePresence>
  );
}
