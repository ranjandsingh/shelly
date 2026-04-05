use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HotkeyConfig {
    pub modifiers: u32,
    pub key: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppSettings {
    #[serde(default = "default_shell")]
    pub default_shell: String,
    #[serde(default)]
    pub remember_sessions: bool,
    #[serde(default = "default_true")]
    pub auto_check_updates: bool,
    #[serde(default)]
    pub auto_launch_claude: bool,
    #[serde(default)]
    pub auto_start: bool,
    #[serde(default = "default_font_size")]
    pub font_size: u16,
    #[serde(default)]
    pub hotkey: Option<HotkeyConfig>,
    #[serde(default)]
    pub notch_at_bottom: bool,
    #[serde(default = "default_panel_width")]
    pub panel_width: f64,
    #[serde(default = "default_panel_height")]
    pub panel_height: f64,
}

fn default_shell() -> String {
    crate::shell_detect::detect_default_shell()
}
fn default_true() -> bool {
    true
}
fn default_font_size() -> u16 {
    11
}
fn default_panel_width() -> f64 {
    720.0
}
fn default_panel_height() -> f64 {
    400.0
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            default_shell: default_shell(),
            remember_sessions: false,
            auto_check_updates: true,
            auto_launch_claude: false,
            auto_start: false,
            font_size: default_font_size(),
            hotkey: None,
            notch_at_bottom: false,
            panel_width: default_panel_width(),
            panel_height: default_panel_height(),
        }
    }
}

fn settings_dir() -> PathBuf {
    let dir = dirs::config_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("shelly");
    fs::create_dir_all(&dir).ok();
    dir
}

fn settings_path() -> PathBuf {
    settings_dir().join("settings.json")
}

pub fn load_settings() -> AppSettings {
    let path = settings_path();
    if let Ok(data) = fs::read_to_string(&path) {
        serde_json::from_str(&data).unwrap_or_default()
    } else {
        AppSettings::default()
    }
}

pub fn save_settings(settings: &AppSettings) {
    let path = settings_path();
    if let Ok(json) = serde_json::to_string_pretty(settings) {
        let _ = fs::write(path, json);
    }
}

// --- Session persistence ---

use crate::session_store::TerminalSession;

fn sessions_path() -> PathBuf {
    settings_dir().join("sessions.json")
}

pub fn save_sessions(sessions: &[TerminalSession]) {
    let path = sessions_path();
    let json = serde_json::to_string_pretty(sessions).unwrap_or_default();
    let _ = fs::write(path, json);
}

pub fn load_sessions() -> Vec<TerminalSession> {
    let path = sessions_path();
    if let Ok(data) = fs::read_to_string(&path) {
        serde_json::from_str(&data).unwrap_or_default()
    } else {
        Vec::new()
    }
}
