import { useRef } from "react";
import { useTerminal } from "../hooks/useTerminal";
import "xterm/css/xterm.css";

interface TerminalViewProps {
  sessionId: string | null;
  workingDirectory?: string;
}

export function TerminalView({ sessionId, workingDirectory }: TerminalViewProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  useTerminal(containerRef, sessionId, workingDirectory);

  return (
    <div
      ref={containerRef}
      style={{
        width: "100%",
        height: "100%",
        background: "#1a1a1a",
        paddingLeft: 6,
        flex: 1,
        minHeight: 0,
      }}
    />
  );
}
