import { useState, useEffect } from "react";
import { invoke } from "@tauri-apps/api/core";
import { THEMES } from "../lib/themes";

const IS_MAC = typeof navigator !== "undefined" && navigator.platform.toLowerCase().includes("mac");

function parseAccelerator(accel: string): { ctrl: boolean; alt: boolean; shift: boolean; cmd: boolean; key: string } {
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

function prettyAccelerator(accel: string): string {
  const p = parseAccelerator(accel);
  const parts: string[] = [];
  if (IS_MAC) {
    if (p.ctrl) parts.push("\u2303");
    if (p.alt) parts.push("\u2325");
    if (p.shift) parts.push("\u21E7");
    if (p.cmd) parts.push("\u2318");
  } else {
    if (p.ctrl) parts.push("Ctrl");
    if (p.alt) parts.push("Alt");
    if (p.shift) parts.push("Shift");
    if (p.cmd) parts.push("Win");
  }
  if (p.key) parts.push(p.key);
  return parts.join(IS_MAC ? "" : "+");
}

interface SettingsMenuProps {
  onClose: () => void;
  onThemeChange: (themeId: string) => void;
  onFontSizeChange: (size: number) => void;
  currentTheme: string;
  currentFontSize: number;
  showHints: boolean;
  onToggleHints: () => void;
}

interface ShellInfo {
  label: string;
  path: string;
}

interface AppSettings {
  defaultShell: string;
  rememberSessions: boolean;
  autoCheckUpdates: boolean;
  autoLaunchClaude: boolean;
  autoStart: boolean;
  fontSize: number;
  notchAtBottom: boolean;
}

interface AttentionSettings {
  enabled: boolean;
  triggerStatuses: string[];
  stealFocus: boolean;
  autoHideTimeoutMs: number;
}

const ALL_TRIGGER_STATUSES: string[] = ["TaskCompleted", "WaitingForInput", "Interrupted"];

export function SettingsMenu({
  onClose,
  onThemeChange,
  onFontSizeChange,
  currentTheme,
  currentFontSize,
  showHints,
  onToggleHints,
}: SettingsMenuProps) {
  const [subMenu, setSubMenu] = useState<string | null>(null);
  const [shells, setShells] = useState<ShellInfo[]>([]);
  const [currentShell, setCurrentShell] = useState("");
  const [settings, setSettings] = useState<AppSettings | null>(null);
  const [attention, setAttention] = useState<AttentionSettings | null>(null);
  const [hotkey, setHotkeyState] = useState<string>("CmdOrCtrl+`");
  const [captureMods, setCaptureMods] = useState<{ ctrl: boolean; alt: boolean; shift: boolean; cmd: boolean }>({ ctrl: false, alt: false, shift: false, cmd: false });
  const [captureKey, setCaptureKey] = useState<string>("");
  const [hotkeyError, setHotkeyError] = useState<string>("");

  useEffect(() => {
    invoke<ShellInfo[]>("get_available_shells_cmd").then(setShells);
    invoke<string>("get_default_shell").then(setCurrentShell);
    invoke<AppSettings>("get_settings").then(setSettings);
    invoke<string>("get_hotkey").then(setHotkeyState);
    invoke<AttentionSettings>("get_attention_settings").then(setAttention);
  }, []);

  const updateSetting = async (key: string, value: any) => {
    if (!settings) return;
    const updated = { ...settings, [key]: value };
    setSettings(updated);
    await invoke("save_app_settings", { newSettings: updated });
  };

  const updateAttention = async (patch: Partial<AttentionSettings>) => {
    if (!attention) return;
    const updated = { ...attention, ...patch };
    if (updated.autoHideTimeoutMs < 1000) updated.autoHideTimeoutMs = 1000;
    if (updated.autoHideTimeoutMs > 30000) updated.autoHideTimeoutMs = 30000;
    setAttention(updated);
    await invoke("set_attention_settings", { newAttention: updated });
  };

  const setShell = async (path: string) => {
    setCurrentShell(path);
    await invoke("set_default_shell", { path });
    if (settings) {
      await updateSetting("defaultShell", path);
    }
    setSubMenu(null);
  };

  const setFontSize = async (size: number) => {
    onFontSizeChange(size);
    if (settings) {
      await updateSetting("fontSize", size);
    }
    setSubMenu(null);
  };

  const setTheme = (themeId: string) => {
    onThemeChange(themeId);
    setSubMenu(null);
  };

  const handleCollapse = (e: React.MouseEvent) => {
    e.stopPropagation();
    invoke("hide_panel");
    onClose();
  };

  const handleQuit = (e: React.MouseEvent) => {
    e.stopPropagation();
    onClose();
    invoke("quit_shelly").catch(() => window.close());
  };

  const toggleSetting = (key: string, currentValue: boolean) => {
    const newVal = !currentValue;
    updateSetting(key, newVal);
    if (key === "autoStart") {
      invoke("set_auto_start_cmd", { enabled: newVal });
    }
  };

  // Sub-menu renderers
  if (subMenu === "hotkey") {
    const anyMod = captureMods.ctrl || captureMods.alt || captureMods.shift || captureMods.cmd;
    const preview = buildAccelerator(captureMods, captureKey);
    const canSave = anyMod && captureKey.length > 0;

    const toggleMod = (mod: keyof typeof captureMods) => {
      setCaptureMods((m) => ({ ...m, [mod]: !m[mod] }));
      setHotkeyError("");
    };

    const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
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
      setCaptureKey(label);
      setHotkeyError("");
    };

    const handleSave = async () => {
      if (!canSave) {
        setHotkeyError("Pick at least one modifier and one key.");
        return;
      }
      try {
        await invoke("set_hotkey", { accelerator: preview });
        setHotkeyState(preview);
        setSubMenu(null);
      } catch (e: any) {
        setHotkeyError(String(e));
      }
    };

    const handleReset = async () => {
      try {
        await invoke("set_hotkey", { accelerator: "CmdOrCtrl+`" });
        setHotkeyState("CmdOrCtrl+`");
        setSubMenu(null);
      } catch (e: any) {
        setHotkeyError(String(e));
      }
    };

    return (
      <div
        className="settings-menu"
        tabIndex={0}
        onClick={(e) => e.stopPropagation()}
        onKeyDown={onKeyDown}
      >
        <div className="menu-item back" onClick={() => setSubMenu(null)}>
          &#x2190; Trigger Shortcut
        </div>
        <div className="menu-separator" />
        <div className="menu-item" style={{ gap: 8, justifyContent: "space-between" }}>
          <div style={{ display: "flex", gap: 6 }}>
            <button onClick={() => toggleMod("ctrl")} className={captureMods.ctrl ? "mod-on" : ""}>Ctrl</button>
            <button onClick={() => toggleMod("alt")} className={captureMods.alt ? "mod-on" : ""}>{IS_MAC ? "Opt" : "Alt"}</button>
            <button onClick={() => toggleMod("shift")} className={captureMods.shift ? "mod-on" : ""}>Shift</button>
            <button onClick={() => toggleMod("cmd")} className={captureMods.cmd ? "mod-on" : ""}>{IS_MAC ? "Cmd" : "Win"}</button>
          </div>
        </div>
        <div className="menu-item">
          Key: <span className="menu-value">{captureKey || "press any key"}</span>
        </div>
        <div className="menu-item">
          Preview: <span className="menu-value">{preview || "\u2014"}</span>
        </div>
        {hotkeyError && <div className="menu-item" style={{ color: "#ef5350" }}>{hotkeyError}</div>}
        <div className="menu-separator" />
        <div className="menu-item" onClick={handleSave} style={{ opacity: canSave ? 1 : 0.5 }}>Save</div>
        <div className="menu-item" onClick={handleReset}>Reset to default</div>
      </div>
    );
  }

  if (subMenu === "shell") {
    return (
      <div className="settings-menu" onClick={(e) => e.stopPropagation()}>
        <div className="menu-item back" onClick={() => setSubMenu(null)}>
          &#x2190; Default Shell
        </div>
        <div className="menu-separator" />
        {shells.map((s) => (
          <div
            key={s.path}
            className={`menu-item ${s.path === currentShell ? "checked" : ""}`}
            onClick={() => setShell(s.path)}
          >
            {s.path === currentShell && <span className="check">&#x2713;</span>}
            {s.label}
          </div>
        ))}
      </div>
    );
  }

  if (subMenu === "fontSize") {
    const sizes = [
      { label: "Small", size: 9 },
      { label: "Medium", size: 11 },
      { label: "Large", size: 14 },
      { label: "Extra Large", size: 18 },
    ];
    return (
      <div className="settings-menu" onClick={(e) => e.stopPropagation()}>
        <div className="menu-item back" onClick={() => setSubMenu(null)}>
          &#x2190; Text Size
        </div>
        <div className="menu-separator" />
        {sizes.map((s) => (
          <div
            key={s.size}
            className={`menu-item ${currentFontSize === s.size ? "checked" : ""}`}
            onClick={() => setFontSize(s.size)}
          >
            {currentFontSize === s.size && <span className="check">&#x2713;</span>}
            {s.label} ({s.size}px)
          </div>
        ))}
      </div>
    );
  }

  if (subMenu === "theme") {
    return (
      <div className="settings-menu" onClick={(e) => e.stopPropagation()}>
        <div className="menu-item back" onClick={() => setSubMenu(null)}>
          &#x2190; Theme
        </div>
        <div className="menu-separator" />
        {Object.entries(THEMES).map(([id, theme]) => (
          <div
            key={id}
            className={`menu-item ${currentTheme === id ? "checked" : ""}`}
            onClick={() => setTheme(id)}
          >
            {currentTheme === id && <span className="check">&#x2713;</span>}
            <span
              className="theme-preview"
              style={{ background: theme.terminal.background, borderColor: theme.chrome.border }}
            />
            {theme.name}
          </div>
        ))}
      </div>
    );
  }

  if (subMenu === "attention") {
    if (!attention) return null;
    const toggleStatus = (st: string) => {
      const set = new Set(attention.triggerStatuses);
      if (set.has(st)) set.delete(st);
      else set.add(st);
      updateAttention({ triggerStatuses: Array.from(set) });
    };
    return (
      <div className="settings-menu" onClick={(e) => e.stopPropagation()}>
        <div className="menu-item back" onClick={() => setSubMenu(null)}>
          &#x2190; Attention
        </div>
        <div className="menu-separator" />
        <div
          className={`menu-item toggle ${attention.enabled ? "on" : ""}`}
          onClick={() => updateAttention({ enabled: !attention.enabled })}
        >
          Auto-show on status
          <span className="toggle-indicator">{attention.enabled ? "ON" : "OFF"}</span>
        </div>
        <div
          className={`menu-item toggle ${attention.stealFocus ? "on" : ""}`}
          onClick={() => updateAttention({ stealFocus: !attention.stealFocus })}
        >
          Steal focus
          <span className="toggle-indicator">{attention.stealFocus ? "ON" : "OFF"}</span>
        </div>
        <div className="menu-separator" />
        {ALL_TRIGGER_STATUSES.map((st) => {
          const active = attention.triggerStatuses.includes(st);
          return (
            <div
              key={st}
              className={`menu-item ${active ? "checked" : ""}`}
              onClick={() => toggleStatus(st)}
            >
              {active && <span className="check">&#x2713;</span>}
              {st}
            </div>
          );
        })}
        <div className="menu-separator" />
        <div className="menu-item" style={{ gap: 8 }}>
          Auto-hide (ms)
          <input
            type="number"
            min={1000}
            max={30000}
            step={500}
            value={attention.autoHideTimeoutMs}
            onChange={(e) => {
              const v = parseInt(e.target.value, 10);
              if (!Number.isNaN(v)) updateAttention({ autoHideTimeoutMs: v });
            }}
            style={{ width: 80, marginLeft: "auto" }}
          />
        </div>
      </div>
    );
  }

  return (
    <div className="settings-menu" onClick={(e) => e.stopPropagation()}>
      <div className="menu-item" onClick={handleCollapse}>
        Collapse to bar
      </div>
      <div className="menu-item arrow" onClick={() => setSubMenu("theme")}>
        Theme
        <span className="menu-value">{THEMES[currentTheme]?.name}</span>
      </div>
      <div className="menu-item arrow" onClick={() => setSubMenu("fontSize")}>
        Text Size
        <span className="menu-value">{currentFontSize}px</span>
      </div>
      <div className="menu-item arrow" onClick={() => setSubMenu("shell")}>
        Default Shell
        <span className="menu-value">
          {shells.find((s) => s.path === currentShell)?.label || "..."}
        </span>
      </div>
      <div
        className="menu-item arrow"
        onClick={() => {
          const parsed = parseAccelerator(hotkey);
          setCaptureMods({ ctrl: parsed.ctrl, alt: parsed.alt, shift: parsed.shift, cmd: parsed.cmd });
          setCaptureKey(parsed.key);
          setHotkeyError("");
          setSubMenu("hotkey");
        }}
      >
        Trigger Shortcut
        <span className="menu-value">{prettyAccelerator(hotkey)}</span>
      </div>
      <div className="menu-item arrow" onClick={() => setSubMenu("attention")}>
        Attention
        <span className="menu-value">{attention?.enabled ? "ON" : "OFF"}</span>
      </div>
      {settings && (
        <>
          <div
            className={`menu-item toggle ${settings.autoLaunchClaude ? "on" : ""}`}
            onClick={() => toggleSetting("autoLaunchClaude", settings.autoLaunchClaude)}
          >
            Auto-launch Claude
            <span className="toggle-indicator">{settings.autoLaunchClaude ? "ON" : "OFF"}</span>
          </div>
          <div
            className={`menu-item toggle ${settings.rememberSessions ? "on" : ""}`}
            onClick={() => toggleSetting("rememberSessions", settings.rememberSessions)}
          >
            Remember Sessions
            <span className="toggle-indicator">{settings.rememberSessions ? "ON" : "OFF"}</span>
          </div>
          <div
            className={`menu-item toggle ${settings.autoStart ? "on" : ""}`}
            onClick={() => toggleSetting("autoStart", settings.autoStart)}
          >
            Start with System
            <span className="toggle-indicator">{settings.autoStart ? "ON" : "OFF"}</span>
          </div>
          <div
            className={`menu-item toggle ${settings.autoCheckUpdates ? "on" : ""}`}
            onClick={() => toggleSetting("autoCheckUpdates", settings.autoCheckUpdates)}
          >
            Auto-check Updates
            <span className="toggle-indicator">{settings.autoCheckUpdates ? "ON" : "OFF"}</span>
          </div>
          <div
            className={`menu-item toggle ${showHints ? "on" : ""}`}
            onClick={onToggleHints}
          >
            Show Hints
            <span className="toggle-indicator">{showHints ? "ON" : "OFF"}</span>
          </div>
        </>
      )}
      <div className="menu-item danger" onClick={handleQuit}>
        Quit Shelly
      </div>
    </div>
  );
}
