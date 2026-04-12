import "./UpdateBanner.css";

export type UpdateBannerState =
  | { kind: "checking" }
  | { kind: "downloading"; version: string }
  | { kind: "ready"; version: string }
  | { kind: "uptodate" }
  | { kind: "error"; message: string };

interface Props {
  state: UpdateBannerState;
  onRestart: () => void;
  onDismiss: () => void;
}

export function UpdateBanner({ state, onRestart, onDismiss }: Props) {
  const { text, action, tone } = render(state, onRestart);
  return (
    <div className={`update-banner update-banner-${tone}`} role="status">
      <span className="update-banner-text">{text}</span>
      {action}
      <button className="update-banner-dismiss" onClick={onDismiss} aria-label="Dismiss">×</button>
    </div>
  );
}

function render(state: UpdateBannerState, onRestart: () => void): {
  text: string;
  action: React.ReactNode;
  tone: "info" | "success" | "error";
} {
  switch (state.kind) {
    case "checking":
      return { text: "Checking for updates…", action: null, tone: "info" };
    case "downloading":
      return { text: `Downloading v${state.version}…`, action: null, tone: "info" };
    case "ready":
      return {
        text: `Update v${state.version} ready.`,
        action: (
          <button className="update-banner-action" onClick={onRestart}>
            Restart to install
          </button>
        ),
        tone: "success",
      };
    case "uptodate":
      return { text: "You're up to date.", action: null, tone: "success" };
    case "error":
      return { text: `Update failed: ${state.message}`, action: null, tone: "error" };
  }
}
