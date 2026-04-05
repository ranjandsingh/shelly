import { TerminalSession } from "../hooks/useSessionStore";

interface NotchProps {
  sessions: TerminalSession[];
  onMouseEnter: () => void;
  onClick: () => void;
}

const STATUS_COLORS: Record<string, string> = {
  Idle: "#666",
  Working: "#4a9eff",
  WaitingForInput: "#f5a623",
  TaskCompleted: "#4caf50",
  Interrupted: "#ef5350",
};

export function Notch({ sessions, onMouseEnter, onClick }: NotchProps) {
  return (
    <div className="notch" onMouseEnter={onMouseEnter} onClick={onClick}>
      <div className="notch-dots">
        {sessions.map((s) => (
          <div
            key={s.id}
            className="notch-dot"
            style={{ background: STATUS_COLORS[s.status] || "#666" }}
          />
        ))}
      </div>
    </div>
  );
}
