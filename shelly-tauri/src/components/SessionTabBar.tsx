import { TerminalSession } from "../hooks/useSessionStore";

interface SessionTabBarProps {
  sessions: TerminalSession[];
  activeSessionId: string | null;
  onSelect: (id: string) => void;
  onAdd: () => void;
  onClose: (id: string) => void;
}

export function SessionTabBar({
  sessions,
  activeSessionId,
  onSelect,
  onAdd,
  onClose,
}: SessionTabBarProps) {
  return (
    <div className="session-tab-bar">
      {sessions.map((s) => (
        <div
          key={s.id}
          className={`tab ${s.id === activeSessionId ? "active" : ""}`}
          onClick={() => onSelect(s.id)}
        >
          <span className="tab-name">{s.projectName}</span>
          {sessions.length > 1 && (
            <button
              className="tab-close"
              onClick={(e) => {
                e.stopPropagation();
                onClose(s.id);
              }}
            >
              x
            </button>
          )}
        </div>
      ))}
      <button className="tab-add" onClick={onAdd}>
        +
      </button>
    </div>
  );
}
