import { useEffect, useCallback } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { currentMonitor } from "@tauri-apps/api/window";
import { LogicalPosition, LogicalSize } from "@tauri-apps/api/dpi";
import { Notch } from "./Notch";
import { DragBar } from "./DragBar";
import { TerminalSession } from "../hooks/useSessionStore";

interface FloatingPanelProps {
  sessions: TerminalSession[];
  isExpanded: boolean;
  onExpand: () => void;
  onCollapse?: () => void;
  onSettingsClick?: () => void;
  children: React.ReactNode;
}

const NOTCH_WIDTH = 140;
const NOTCH_HEIGHT = 32;
const DEFAULT_WIDTH = 720;
const DEFAULT_HEIGHT = 400;

export function FloatingPanel({
  sessions,
  isExpanded,
  onExpand,
  onCollapse: _onCollapse,
  onSettingsClick,
  children,
}: FloatingPanelProps) {
  const appWindow = getCurrentWindow();

  const positionCenter = useCallback(
    async (width: number, height: number) => {
      const monitor = await currentMonitor();
      if (!monitor) return;
      const screenWidth = monitor.size.width / monitor.scaleFactor;
      const x = Math.round((screenWidth - width) / 2);
      await appWindow.setSize(new LogicalSize(width, height));
      await appWindow.setPosition(new LogicalPosition(x, 0));
    },
    [appWindow]
  );

  // Position and resize window based on expanded state
  useEffect(() => {
    const update = async () => {
      if (isExpanded) {
        console.log("[FloatingPanel] expanding to", DEFAULT_WIDTH, DEFAULT_HEIGHT);
        await positionCenter(DEFAULT_WIDTH, DEFAULT_HEIGHT);
        await appWindow.setResizable(true);
        await appWindow.show();
        await appWindow.setFocus();
      } else {
        console.log("[FloatingPanel] collapsing to notch");
        await appWindow.setResizable(false);
        await positionCenter(NOTCH_WIDTH, NOTCH_HEIGHT);
        await appWindow.show();
      }
    };
    update();
  }, [isExpanded, positionCenter, appWindow]);

  // Start collapsed (notch) on mount
  useEffect(() => {
    const init = async () => {
      console.log("[FloatingPanel] init: showing notch");
      await appWindow.setResizable(false);
      await positionCenter(NOTCH_WIDTH, NOTCH_HEIGHT);
      await appWindow.show();
    };
    init();
  }, []);

  const handleResizeMouseDown = useCallback(
    async (e: React.MouseEvent) => {
      e.preventDefault();
      await (appWindow as any).startResizeDragging("SouthEast");
    },
    [appWindow]
  );

  if (!isExpanded) {
    return (
      <div className="notch-container">
        <Notch
          sessions={sessions}
          onMouseEnter={onExpand}
          onClick={onExpand}
        />
      </div>
    );
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
      >
        <DragBar onSettingsClick={onSettingsClick} />
        {children}
        <div className="resize-grip" onMouseDown={handleResizeMouseDown} />
      </motion.div>
    </AnimatePresence>
  );
}
