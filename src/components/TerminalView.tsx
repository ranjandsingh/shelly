import { useRef } from "react";
import { useTerminal } from "../hooks/useTerminal";
import "xterm/css/xterm.css";

interface TerminalViewProps {
  sessionId: string | null;
  workingDirectory?: string;
  theme?: any;
  fontSize?: number;
}

export function TerminalView({ sessionId, workingDirectory, theme, fontSize }: TerminalViewProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  useTerminal(containerRef, sessionId, workingDirectory, theme, fontSize);

  return (
    <div
      style={{
        width: "100%",
        height: "100%",
        flex: 1,
        minHeight: 0,
        padding: "0 6px",
        boxSizing: "border-box",
      }}
    >
      <div
        ref={containerRef}
        style={{ width: "100%", height: "100%" }}
      />
    </div>
  );
}
