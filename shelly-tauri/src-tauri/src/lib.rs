mod pty;
mod shell_detect;

use std::sync::Mutex;
use tauri::{AppHandle, Manager, State};
use uuid::Uuid;

use pty::PtyManager;
use shell_detect::{ShellInfo, detect_default_shell, get_available_shells};

struct AppState {
    pty_manager: PtyManager,
    default_shell: Mutex<String>,
}

#[tauri::command]
fn create_terminal(
    session_id: String,
    working_dir: String,
    cols: u16,
    rows: u16,
    state: State<'_, AppState>,
    app: AppHandle,
) -> Result<(), String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    let shell = state.default_shell.lock().unwrap().clone();
    state.pty_manager.create(id, &working_dir, &shell, cols, rows, app)
}

#[tauri::command]
fn write_input(session_id: String, data: String, state: State<'_, AppState>) -> Result<(), String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    state.pty_manager.write_input(id, data.as_bytes())
}

#[tauri::command]
fn resize_terminal(session_id: String, cols: u16, rows: u16, state: State<'_, AppState>) -> Result<(), String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    state.pty_manager.resize(id, cols, rows)
}

#[tauri::command]
fn get_buffered_output(session_id: String, state: State<'_, AppState>) -> Result<String, String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    state.pty_manager.get_buffered_output(id)
}

#[tauri::command]
fn suppress_live_output(session_id: String, suppress: bool, state: State<'_, AppState>) -> Result<(), String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    state.pty_manager.suppress_live_output(id, suppress)
}

#[tauri::command]
fn destroy_terminal(session_id: String, state: State<'_, AppState>) {
    if let Ok(id) = Uuid::parse_str(&session_id) {
        state.pty_manager.destroy(id);
    }
}

#[tauri::command]
fn has_terminal(session_id: String, state: State<'_, AppState>) -> bool {
    Uuid::parse_str(&session_id)
        .map(|id| state.pty_manager.has_terminal(id))
        .unwrap_or(false)
}

#[tauri::command]
fn get_available_shells_cmd() -> Vec<ShellInfo> {
    get_available_shells()
}

#[tauri::command]
fn get_default_shell(state: State<'_, AppState>) -> String {
    state.default_shell.lock().unwrap().clone()
}

#[tauri::command]
fn set_default_shell(path: String, state: State<'_, AppState>) {
    *state.default_shell.lock().unwrap() = path;
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    env_logger::init();

    let default_shell = detect_default_shell();
    log::info!("Default shell: {default_shell}");

    tauri::Builder::default()
        .manage(AppState {
            pty_manager: PtyManager::new(),
            default_shell: Mutex::new(default_shell),
        })
        .plugin(tauri_plugin_opener::init())
        .plugin(
            tauri_plugin_global_shortcut::Builder::new().build(),
        )
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            if let Some(w) = app.get_webview_window("main") {
                let _ = w.show();
                let _ = w.set_focus();
            }
        }))
        .invoke_handler(tauri::generate_handler![
            create_terminal,
            write_input,
            resize_terminal,
            get_buffered_output,
            suppress_live_output,
            destroy_terminal,
            has_terminal,
            get_available_shells_cmd,
            get_default_shell,
            set_default_shell,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
