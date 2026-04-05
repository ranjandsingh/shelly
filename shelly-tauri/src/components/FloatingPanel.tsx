import { useEffect } from "react";
import { invoke } from "@tauri-apps/api/core";
import { motion } from "framer-motion";
import { DragBar } from "./DragBar";

interface FloatingPanelProps {
  isExpanded: boolean;
  onSettingsClick?: () => void;
  children: React.ReactNode;
}

export function FloatingPanel({
  isExpanded,
  onSettingsClick,
  children,
}: FloatingPanelProps) {
  // When React state says expanded, position the window via Rust
  useEffect(() => {
    if (isExpanded) {
      console.log("[FloatingPanel] isExpanded=true, positioning via Rust");
      invoke("position_panel_center").catch((e) =>
        console.error("[FloatingPanel] position error:", e)
      );
    }
  }, [isExpanded]);

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
    </motion.div>
  );
}
