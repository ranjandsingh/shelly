import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";

const IS_MAC = typeof navigator !== "undefined" && navigator.platform.toLowerCase().includes("mac");

interface HotkeyCaptureModalProps {
  initial: string;
  onSaved: (accel: string) => void;
  onClose: () => void;
}

function parseAccelerator(accel: string) {
  const parts = accel.split("+").map((p) => p.trim());
  const mods = { ctrl: false, alt: false, shift: false, cmd: false };
  let key = "";
  for (const p of parts) {
    const lower = p.toLowerCase();
    if (lower === "ctrl" || lower === "control") mods.ctrl = true;
    else if (lower === "alt" || lower === "option") mods.alt = true;
    else if (lower === "shift") mods.shift = true;
    else if (lower === "cmd" || lower === "command" || lower === "meta" || lower === "super") mods.cmd = true;
    else if (lower === "cmdorctrl") { if (IS_MAC) mods.cmd = true; else mods.ctrl = true; }
    else key = p;
  }
  return { ...mods, key };
}

function buildAccelerator(mods: { ctrl: boolean; alt: boolean; shift: boolean; cmd: boolean }, key: string): string {
  const parts: string[] = [];
  if (mods.ctrl) parts.push("Ctrl");
  if (mods.alt) parts.push("Alt");
  if (mods.shift) parts.push("Shift");
  if (mods.cmd) parts.push(IS_MAC ? "Cmd" : "Super");
  if (key) parts.push(key);
  return parts.join("+");
}

export function HotkeyCaptureModal({ initial, onSaved, onClose }: HotkeyCaptureModalProps) {
  const parsed = parseAccelerator(initial);
  const [mods, setMods] = useState({ ctrl: parsed.ctrl, alt: parsed.alt, shift: parsed.shift, cmd: parsed.cmd });
  const [key, setKey] = useState(parsed.key);
  const [error, setError] = useState("");

  const anyMod = mods.ctrl || mods.alt || mods.shift || mods.cmd;
  const preview = buildAccelerator(mods, key);
  const canSave = anyMod && key.length > 0;

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
        return;
      }
      if (["Control", "Alt", "Shift", "Meta"].includes(e.key)) return;
      e.preventDefault();
      const code = e.code;
      let label = e.key;
      if (code === "Backquote") label = "`";
      else if (code === "Space") label = "Space";
      else if (/^Key[A-Z]$/.test(code)) label = code.slice(3);
      else if (/^Digit\d$/.test(code)) label = code.slice(5);
      else if (code.startsWith("F") && /^F\d+$/.test(code)) label = code;
      else if (e.key.length === 1) label = e.key.toUpperCase();
      setKey(label);
      setError("");
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [onClose]);

  const toggleMod = (m: keyof typeof mods) => {
    setMods((prev) => ({ ...prev, [m]: !prev[m] }));
    setError("");
  };

  const handleSave = async () => {
    if (!canSave) {
      setError("Pick at least one modifier and one key.");
      return;
    }
    try {
      await invoke("set_hotkey", { accelerator: preview });
      onSaved(preview);
      onClose();
    } catch (e: any) {
      setError(String(e));
    }
  };

  const handleReset = async () => {
    try {
      await invoke("set_hotkey", { accelerator: "CmdOrCtrl+`" });
      onSaved("CmdOrCtrl+`");
      onClose();
    } catch (e: any) {
      setError(String(e));
    }
  };

  return (
    <div
      onClick={onClose}
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0, 0, 0, 0.55)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
        backdropFilter: "blur(4px)",
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          background: "#1f1f1f",
          border: "1px solid rgba(255, 255, 255, 0.12)",
          borderRadius: 10,
          padding: "20px 22px",
          minWidth: 360,
          maxWidth: 440,
          color: "#e6e6e6",
          boxShadow: "0 20px 60px rgba(0, 0, 0, 0.6)",
          fontFamily: "'Segoe UI', -apple-system, sans-serif",
        }}
      >
        <div style={{ fontSize: 14, fontWeight: 600, marginBottom: 10 }}>
          Set Trigger Shortcut
        </div>
        <ol style={{ fontSize: 12, opacity: 0.75, margin: "0 0 16px 18px", padding: 0, lineHeight: 1.6 }}>
          <li>Pick one or more modifier keys below.</li>
          <li>Press any other key to capture it.</li>
          <li>Click <b>Save</b> to apply.</li>
        </ol>

        <div style={{ display: "flex", gap: 6, marginBottom: 12 }}>
          {(["ctrl", "alt", "shift", "cmd"] as const).map((m) => (
            <button
              key={m}
              onClick={() => toggleMod(m)}
              style={{
                flex: 1,
                padding: "6px 0",
                borderRadius: 6,
                border: "1px solid rgba(255, 255, 255, 0.15)",
                background: mods[m] ? "#2b6ee5" : "#2a2a2a",
                color: "#e6e6e6",
                fontSize: 12,
                cursor: "pointer",
                transition: "background 0.15s",
              }}
            >
              {m === "ctrl" ? "Ctrl"
                : m === "alt" ? (IS_MAC ? "Opt" : "Alt")
                : m === "shift" ? "Shift"
                : IS_MAC ? "Cmd" : "Win"}
            </button>
          ))}
        </div>

        <div style={{ display: "flex", gap: 10, alignItems: "center", marginBottom: 6, fontSize: 12 }}>
          <span style={{ opacity: 0.65, minWidth: 52 }}>Key</span>
          <span style={{
            flex: 1,
            padding: "6px 10px",
            background: "#2a2a2a",
            borderRadius: 6,
            border: "1px solid rgba(255, 255, 255, 0.1)",
            fontFamily: "monospace",
            fontSize: 12,
          }}>
            {key || <span style={{ opacity: 0.5 }}>press any key...</span>}
          </span>
        </div>

        <div style={{ display: "flex", gap: 10, alignItems: "center", marginBottom: 14, fontSize: 12 }}>
          <span style={{ opacity: 0.65, minWidth: 52 }}>Preview</span>
          <span style={{
            flex: 1,
            padding: "6px 10px",
            background: "#2a2a2a",
            borderRadius: 6,
            border: "1px solid rgba(255, 255, 255, 0.1)",
            fontFamily: "monospace",
            fontSize: 12,
            color: canSave ? "#8de08d" : "#e6e6e6",
          }}>
            {preview || "\u2014"}
          </span>
        </div>

        {error && (
          <div style={{ color: "#ef5350", fontSize: 12, marginBottom: 10 }}>{error}</div>
        )}

        <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
          <button onClick={onClose} style={btnStyle("#3a3a3a")}>Cancel</button>
          <button onClick={handleReset} style={btnStyle("#3a3a3a")}>Reset</button>
          <button
            onClick={handleSave}
            disabled={!canSave}
            style={{ ...btnStyle(canSave ? "#2b6ee5" : "#2a2a2a"), opacity: canSave ? 1 : 0.5 }}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}

function btnStyle(bg: string): React.CSSProperties {
  return {
    padding: "7px 14px",
    borderRadius: 6,
    border: "1px solid rgba(255, 255, 255, 0.12)",
    background: bg,
    color: "#e6e6e6",
    fontSize: 12,
    cursor: "pointer",
  };
}
