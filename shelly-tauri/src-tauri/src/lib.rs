mod pty;
mod session_store;
mod shell_detect;
mod sleep_prevention;
mod sound;
mod status_parser;
mod tray;

use std::sync::Mutex;
use tauri::{AppHandle, Emitter, Manager, State};
use uuid::Uuid;

use pty::PtyManager;
use session_store::{SessionStore, TerminalSession};
use shell_detect::{ShellInfo, detect_default_shell, get_available_shells};
use status_parser::StatusParser;

struct AppState {
    pty_manager: PtyManager,
    session_store: SessionStore,
    status_parser: StatusParser,
    default_shell: Mutex<String>,
}

// --- Terminal commands ---

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
    state.session_store.set_session_started(&session_id);
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

// --- Session commands ---

#[tauri::command]
fn get_sessions(state: State<'_, AppState>) -> Vec<TerminalSession> {
    state.session_store.get_sessions()
}

#[tauri::command]
fn get_active_session_id(state: State<'_, AppState>) -> Option<String> {
    state.session_store.get_active_session_id()
}

#[tauri::command]
fn add_session(
    name: Option<String>,
    project_path: Option<String>,
    working_dir: Option<String>,
    state: State<'_, AppState>,
) -> TerminalSession {
    state.session_store.add_session(name, project_path, working_dir)
}

#[tauri::command]
fn select_session(session_id: String, state: State<'_, AppState>) {
    state.session_store.select_session(&session_id);
}

#[tauri::command]
fn remove_session(session_id: String, state: State<'_, AppState>) -> Option<String> {
    if let Ok(id) = Uuid::parse_str(&session_id) {
        state.pty_manager.destroy(id);
    }
    state.session_store.remove_session(&session_id)
}

#[tauri::command]
fn rename_session(session_id: String, name: String, state: State<'_, AppState>) {
    state.session_store.rename_session(&session_id, &name);
}

#[tauri::command]
fn get_session(session_id: String, state: State<'_, AppState>) -> Option<TerminalSession> {
    state.session_store.get_session(&session_id)
}

// --- Status commands ---

#[tauri::command]
fn parse_visible_text(session_id: String, text: String, state: State<'_, AppState>, app: AppHandle) {
    state.status_parser.parse_visible_text(&session_id, &text, &state.session_store, &app);
}

// --- Shell commands ---

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

    let session_store = SessionStore::new();
    session_store.ensure_default_session();

    tauri::Builder::default()
        .manage(AppState {
            pty_manager: PtyManager::new(),
            session_store,
            status_parser: StatusParser::new(),
            default_shell: Mutex::new(default_shell),
        })
        .plugin(tauri_plugin_opener::init())
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(|app, _shortcut, event| {
                    if event.state == tauri_plugin_global_shortcut::ShortcutState::Pressed {
                        let _ = app.emit("tray-toggle-panel", ());
                        if let Some(window) = app.get_webview_window("main") {
                            let _ = window.show();
                            let _ = window.set_focus();
                        }
                    }
                })
                .build(),
        )
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            if let Some(w) = app.get_webview_window("main") {
                let _ = w.show();
                let _ = w.set_focus();
            }
        }))
        .setup(|app| {
            tray::setup_tray(app.handle())?;

            // Register default hotkey: CmdOrCtrl+`
            use tauri_plugin_global_shortcut::GlobalShortcutExt;
            if let Err(e) = app.global_shortcut().register("CmdOrCtrl+`") {
                log::warn!("Failed to register global shortcut: {e}");
            }

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            create_terminal,
            write_input,
            resize_terminal,
            get_buffered_output,
            suppress_live_output,
            destroy_terminal,
            has_terminal,
            get_sessions,
            get_active_session_id,
            add_session,
            select_session,
            remove_session,
            rename_session,
            get_session,
            parse_visible_text,
            get_available_shells_cmd,
            get_default_shell,
            set_default_shell,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
