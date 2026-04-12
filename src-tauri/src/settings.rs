use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppSettings {
    #[serde(default = "default_shell")]
    pub default_shell: String,
    #[serde(default = "default_true")]
    pub remember_sessions: bool,
    #[serde(default = "default_true")]
    pub auto_check_updates: bool,
    #[serde(default)]
    pub auto_launch_claude: bool,
    #[serde(default)]
    pub auto_start: bool,
    #[serde(default = "default_font_size")]
    pub font_size: u16,
    #[serde(default = "default_hotkey", deserialize_with = "deserialize_hotkey")]
    pub hotkey: String,
    #[serde(default)]
    pub notch_at_bottom: bool,
    #[serde(default = "default_panel_width")]
    pub panel_width: f64,
    #[serde(default = "default_panel_height")]
    pub panel_height: f64,
    #[serde(default)]
    pub attention: AttentionSettings,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AttentionSettings {
    #[serde(default = "default_attention_enabled")]
    pub enabled: bool,
    #[serde(default = "default_trigger_statuses")]
    pub trigger_statuses: Vec<TerminalStatus>,
    #[serde(default = "default_steal_focus")]
    pub steal_focus: bool,
    #[serde(default = "default_auto_hide_ms")]
    pub auto_hide_timeout_ms: u64,
}

fn default_attention_enabled() -> bool { true }
fn default_steal_focus() -> bool { true }
fn default_auto_hide_ms() -> u64 { 5000 }
fn default_trigger_statuses() -> Vec<TerminalStatus> {
    vec![TerminalStatus::TaskCompleted, TerminalStatus::WaitingForInput]
}

impl Default for AttentionSettings {
    fn default() -> Self {
        Self {
            enabled: default_attention_enabled(),
            trigger_statuses: default_trigger_statuses(),
            steal_focus: default_steal_focus(),
            auto_hide_timeout_ms: default_auto_hide_ms(),
        }
    }
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
fn default_hotkey() -> String {
    "CmdOrCtrl+`".to_string()
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            default_shell: default_shell(),
            remember_sessions: true,
            auto_check_updates: true,
            auto_launch_claude: false,
            auto_start: false,
            font_size: default_font_size(),
            hotkey: default_hotkey(),
            notch_at_bottom: false,
            panel_width: default_panel_width(),
            panel_height: default_panel_height(),
            attention: AttentionSettings::default(),
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

fn deserialize_hotkey<'de, D>(deserializer: D) -> Result<String, D::Error>
where
    D: serde::Deserializer<'de>,
{
    use serde_json::Value;
    let value = Value::deserialize(deserializer).unwrap_or(Value::Null);
    match value {
        Value::String(s) if !s.is_empty() => Ok(s),
        _ => Ok(default_hotkey()),
    }
}

// --- Session persistence ---

use crate::session_store::TerminalSession;
use crate::session_store::TerminalStatus;

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

impl AttentionSettings {
    /// Clamp the timeout to a sane range.
    pub fn normalized(mut self) -> Self {
        self.auto_hide_timeout_ms = self.auto_hide_timeout_ms.clamp(1000, 30000);
        self
    }
}
