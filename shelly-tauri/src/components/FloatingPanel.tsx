import { useState, useEffect, useRef, useCallback } from "react";
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
  onExpand: (pin: boolean) => void;
  onCollapse: () => void;
  children: React.ReactNode;
}

const COLLAPSED_WIDTH = 120;
const COLLAPSED_HEIGHT = 28;
const DEFAULT_WIDTH = 720;
const DEFAULT_HEIGHT = 400;

export function FloatingPanel({
  sessions,
  isExpanded,
  onExpand,
  onCollapse,
  children,
}: FloatingPanelProps) {
  const [isPinned, setIsPinned] = useState(false);
  const expandedSizeRef = useRef({ w: DEFAULT_WIDTH, h: DEFAULT_HEIGHT });
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

  const doExpand = useCallback(
    async (pin: boolean) => {
      if (isExpanded) return;
      setIsPinned(pin);
      await positionCenter(expandedSizeRef.current.w, expandedSizeRef.current.h);
      await appWindow.setResizable(true);
      onExpand(pin);
    },
    [isExpanded, positionCenter, appWindow, onExpand]
  );

  const doCollapse = useCallback(async () => {
    if (!isExpanded) return;
    setIsPinned(false);
    await appWindow.setResizable(false);
    await positionCenter(COLLAPSED_WIDTH, COLLAPSED_HEIGHT);
    onCollapse();
  }, [isExpanded, positionCenter, appWindow, onCollapse]);

  // Auto-collapse on blur (unless pinned)
  useEffect(() => {
    const handleBlur = () => {
      if (isExpanded && !isPinned) {
        doCollapse();
      }
    };
    window.addEventListener("blur", handleBlur);
    return () => window.removeEventListener("blur", handleBlur);
  }, [isExpanded, isPinned, doCollapse]);

  // Position on mount (collapsed)
  useEffect(() => {
    positionCenter(COLLAPSED_WIDTH, COLLAPSED_HEIGHT).then(() => {
      appWindow.show();
    });
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
      <Notch
        sessions={sessions}
        onMouseEnter={() => doExpand(false)}
        onClick={() => doExpand(true)}
      />
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
        onMouseDown={() => {
          setIsPinned(true);
        }}
      >
        <DragBar />
        {children}
        <div className="resize-grip" onMouseDown={handleResizeMouseDown} />
      </motion.div>
    </AnimatePresence>
  );
}
