use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ImportedTheme {
    pub id: String,
    pub theme: Value,           // The serialised Theme object from the frontend.
    pub imported_at: String,    // ISO 8601 UTC timestamp.
}

fn themes_path() -> PathBuf {
    let dir = dirs::config_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("shelly");
    fs::create_dir_all(&dir).ok();
    dir.join("imported_themes.json")
}

pub fn load_all() -> Vec<ImportedTheme> {
    if let Ok(data) = fs::read_to_string(themes_path()) {
        serde_json::from_str(&data).unwrap_or_default()
    } else {
        Vec::new()
    }
}

pub fn save_all(themes: &[ImportedTheme]) {
    let path = themes_path();
    if let Ok(json) = serde_json::to_string_pretty(themes) {
        let _ = fs::write(path, json);
    }
}

pub fn insert_or_replace(mut theme: ImportedTheme) -> Vec<ImportedTheme> {
    let mut all = load_all();
    if let Some(existing) = all.iter_mut().find(|t| t.id == theme.id) {
        std::mem::swap(existing, &mut theme);
    } else {
        all.push(theme);
    }
    save_all(&all);
    all
}

pub fn delete(id: &str) -> Vec<ImportedTheme> {
    let mut all = load_all();
    all.retain(|t| t.id != id);
    save_all(&all);
    all
}
