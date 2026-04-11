import { useState, useEffect } from "react";
import { invoke } from "@tauri-apps/api/core";
import { THEMES } from "../lib/themes";

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

  useEffect(() => {
    invoke<ShellInfo[]>("get_available_shells_cmd").then(setShells);
    invoke<string>("get_default_shell").then(setCurrentShell);
    invoke<AppSettings>("get_settings").then(setSettings);
  }, []);

  const updateSetting = async (key: string, value: any) => {
    if (!settings) return;
    const updated = { ...settings, [key]: value };
    setSettings(updated);
    await invoke("save_app_settings", { newSettings: updated });
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
