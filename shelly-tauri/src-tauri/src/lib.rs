mod auto_start;
mod ide_detector;
mod pty;
mod session_store;
mod settings;
mod shell_detect;
mod sleep_prevention;
mod sound;
mod status_parser;
mod tray;

use std::sync::Mutex;
use tauri::{AppHandle, Emitter, Listener, Manager, State};
use uuid::Uuid;

use pty::PtyManager;
use session_store::{SessionStore, TerminalSession};
use shell_detect::{ShellInfo, detect_default_shell, get_available_shells};
use status_parser::StatusParser;

struct AppState {
    pty_manager: PtyManager,
    session_store: SessionStore,
    status_parser: StatusParser,
    settings: Mutex<settings::AppSettings>,
    default_shell: Mutex<String>,
    is_pinned: Mutex<bool>,
    hide_cooldown: Mutex<Option<std::time::Instant>>,
    dialog_open: Mutex<bool>,
}

fn do_show_panel(app: &AppHandle) {
    if let Some(main_win) = app.get_webview_window("main") {
        if main_win.is_visible().unwrap_or(false) { return; }
        if let Ok(Some(monitor)) = app.primary_monitor() {
            let sw = monitor.size().width as f64 / monitor.scale_factor();
            let x = ((sw - 720.0) / 2.0) as i32;
            let _ = main_win.set_position(tauri::LogicalPosition::new(x, 0));
        }
        let _ = main_win.show();
        let _ = main_win.set_focus();
        let _ = app.emit("panel-visibility", true);
        log::info!("do_show_panel: shown");
    }
}

fn do_hide_panel(app: &AppHandle) {
    if let Some(main_win) = app.get_webview_window("main") {
        let _ = main_win.hide();
        let _ = app.emit("panel-visibility", false);
        log::info!("do_hide_panel: hidden");
    }
}

fn do_toggle_panel(app: &AppHandle) {
    if let Some(main_win) = app.get_webview_window("main") {
        if main_win.is_visible().unwrap_or(false) {
            do_hide_panel(app);
        } else {
            do_show_panel(app);
        }
    }
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
    log::info!("CMD create_terminal: session={session_id}, dir={working_dir}, {cols}x{rows}");
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;
    let shell = state.default_shell.lock().unwrap().clone();
    log::info!("CMD create_terminal: using shell={shell}");
    state.session_store.set_session_started(&session_id);
    state.pty_manager.create(id, &working_dir, &shell, cols, rows, app)
}

#[tauri::command]
fn write_input(session_id: String, data: String, state: State<'_, AppState>) -> Result<(), String> {
    log::debug!("CMD write_input: session={session_id}, len={}", data.len());
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
    let sessions = state.session_store.get_sessions();
    log::debug!("CMD get_sessions: returning {} sessions", sessions.len());
    sessions
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
    log::info!("CMD add_session: name={:?}, path={:?}, dir={:?}", name, project_path, working_dir);
    let session = state.session_store.add_session(name, project_path, working_dir);
    log::info!("CMD add_session: created id={}", session.id);
    session
}

#[tauri::command]
fn select_session(session_id: String, state: State<'_, AppState>) {
    log::info!("CMD select_session: {session_id}");
    state.session_store.select_session(&session_id);
}

#[tauri::command]
fn remove_session(session_id: String, state: State<'_, AppState>) -> Option<String> {
    log::info!("CMD remove_session: {session_id}");
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

// --- File/folder commands ---

#[tauri::command]
async fn pick_folder(app: AppHandle, state: State<'_, AppState>) -> Result<Option<session_store::TerminalSession>, String> {
    use tauri_plugin_dialog::DialogExt;

    // Suppress blur-hide while dialog is open
    *state.dialog_open.lock().unwrap() = true;

    let folder = app.dialog().file()
        .set_title("Select folder for new terminal session")
        .blocking_pick_folder();

    *state.dialog_open.lock().unwrap() = false;

    if let Some(path) = folder {
        let path_str = path.to_string();
        let folder_name = std::path::Path::new(&path_str)
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| path_str.clone());
        let session = state.session_store.add_session(
            Some(folder_name),
            Some(path_str.clone()),
            Some(path_str),
        );
        state.session_store.select_session(&session.id);
        log::info!("CMD pick_folder: created session {} for {}", session.id, session.working_directory);
        // Re-show and focus the panel
        do_show_panel(&app);
        let _ = app.emit("sessions-force-refresh", ());
        Ok(Some(session))
    } else {
        // Re-show panel even if cancelled
        do_show_panel(&app);
        Ok(None)
    }
}

fn handle_dropped_paths(paths: &[std::path::PathBuf], app: &AppHandle) {
    if let Some(state) = app.try_state::<AppState>() {
        for path in paths {
            let path_str = path.to_string_lossy().into_owned();
            if path.is_dir() {
                let folder_name = path.file_name()
                    .map(|n| n.to_string_lossy().into_owned())
                    .unwrap_or_else(|| path_str.clone());
                let session = state.session_store.add_session(
                    Some(folder_name),
                    Some(path_str.clone()),
                    Some(path_str),
                );
                state.session_store.select_session(&session.id);
                log::info!("DROP: created session for dir {}", session.working_directory);
                do_show_panel(app);
                let _ = app.emit("sessions-force-refresh", ());
            } else if path.is_file() {
                if let Some(active_id) = state.session_store.get_active_session_id() {
                    if let Ok(id) = uuid::Uuid::parse_str(&active_id) {
                        let quoted = if path_str.contains(' ') {
                            format!("\"{}\"", path_str)
                        } else {
                            path_str.clone()
                        };
                        let _ = state.pty_manager.write_input(id, quoted.as_bytes());
                        log::info!("DROP: pasted file path into active terminal");
                    }
                }
            }
        }
    }
}

// --- Status commands ---

#[tauri::command]
fn parse_visible_text(session_id: String, text: String, state: State<'_, AppState>, app: AppHandle) {
    state.status_parser.parse_visible_text(&session_id, &text, &state.session_store, &app);
}

// --- Settings commands ---

#[tauri::command]
fn get_settings(state: State<'_, AppState>) -> settings::AppSettings {
    state.settings.lock().unwrap().clone()
}

#[tauri::command]
fn save_app_settings(new_settings: settings::AppSettings, state: State<'_, AppState>) {
    settings::save_settings(&new_settings);
    // Apply default shell change
    *state.default_shell.lock().unwrap() = new_settings.default_shell.clone();
    *state.settings.lock().unwrap() = new_settings;
}

// --- Auto-start commands ---

#[tauri::command]
fn set_auto_start_cmd(enabled: bool) -> Result<(), String> {
    auto_start::set_auto_start(enabled)
}

// --- IDE detection commands ---

#[tauri::command]
fn detect_ide_projects() -> Vec<ide_detector::DetectedProject> {
    ide_detector::detect_projects()
}

// --- Window commands ---

#[tauri::command]
fn toggle_panel(app: AppHandle) {
    log::info!("CMD toggle_panel");
    do_toggle_panel(&app);
}

#[tauri::command]
fn show_panel(app: AppHandle) {
    log::info!("CMD show_panel");
    do_show_panel(&app);
}

#[tauri::command]
fn hide_panel(app: AppHandle) {
    log::info!("CMD hide_panel");
    do_hide_panel(&app);
}

#[tauri::command]
fn set_pinned(pinned: bool, state: State<'_, AppState>) {
    log::info!("CMD set_pinned: {pinned}");
    *state.is_pinned.lock().unwrap() = pinned;
}

#[tauri::command]
fn get_pinned(state: State<'_, AppState>) -> bool {
    *state.is_pinned.lock().unwrap()
}

#[tauri::command]
fn quit_shelly(app: AppHandle) {
    log::info!("CMD quit_app");
    app.exit(0);
}

#[tauri::command]
fn shrink_notch(app: AppHandle) {
    log::info!("CMD shrink_notch");
    if let Some(notch) = app.get_webview_window("notch") {
        // Shrink to tiny pill
        let _ = notch.set_size(tauri::LogicalSize::new(50.0, 8.0));
        // Re-center
        if let Ok(Some(monitor)) = app.primary_monitor() {
            let sw = monitor.size().width as f64 / monitor.scale_factor();
            let x = ((sw - 50.0) / 2.0) as i32;
            let _ = notch.set_position(tauri::LogicalPosition::new(x, 0));
        }
    }
}

#[tauri::command]
fn expand_notch(app: AppHandle) {
    log::info!("CMD expand_notch");
    if let Some(notch) = app.get_webview_window("notch") {
        let _ = notch.set_size(tauri::LogicalSize::new(100.0, 24.0));
        if let Ok(Some(monitor)) = app.primary_monitor() {
            let sw = monitor.size().width as f64 / monitor.scale_factor();
            let x = ((sw - 100.0) / 2.0) as i32;
            let _ = notch.set_position(tauri::LogicalPosition::new(x, 0));
        }
    }
}

#[tauri::command]
fn position_panel_center(app: AppHandle) {
    if let Some(main_win) = app.get_webview_window("main") {
        if let Ok(Some(monitor)) = app.primary_monitor() {
            let screen_width = monitor.size().width as f64 / monitor.scale_factor();
            let x = ((screen_width - 720.0) / 2.0) as i32;
            let _ = main_win.set_position(tauri::LogicalPosition::new(x, 0));
            log::info!("CMD position_panel_center: x={x}, y=0");
        }
    }
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

    let app_settings = settings::load_settings();
    let default_shell = app_settings.default_shell.clone();
    log::info!("Default shell: {default_shell}");

    let session_store = SessionStore::new();
    session_store.ensure_default_session();

    tauri::Builder::default()
        .manage(AppState {
            pty_manager: PtyManager::new(),
            session_store,
            status_parser: StatusParser::new(),
            settings: Mutex::new(app_settings),
            default_shell: Mutex::new(default_shell),
            is_pinned: Mutex::new(false),
            hide_cooldown: Mutex::new(None),
            dialog_open: Mutex::new(false),
        })
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(|app, _shortcut, event| {
                    if event.state == tauri_plugin_global_shortcut::ShortcutState::Pressed {
                        log::info!("HOTKEY: toggle panel");
                        do_toggle_panel(app);
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
            log::info!("SETUP: initializing tray...");
            tray::setup_tray(app.handle())?;
            log::info!("SETUP: tray initialized");

            // Position notch window at top-center of screen
            if let Some(notch) = app.get_webview_window("notch") {
                log::info!("SETUP: positioning notch window...");
                let handle = app.handle().clone();
                tauri::async_runtime::spawn(async move {
                    if let Some(monitor) = handle.primary_monitor().ok().flatten() {
                        let screen_width = monitor.size().width as f64 / monitor.scale_factor();
                        let x = ((screen_width - 100.0) / 2.0) as i32;
                        let _ = notch.set_position(tauri::LogicalPosition::new(x, 0));
                        log::info!("SETUP: notch positioned at x={x}");
                    }
                    let _ = notch.show();
                });
            }

            // Main window: click-outside-hide (when not pinned)
            // Uses a delay to avoid hiding during resize, menu interactions, etc.
            if let Some(main_win) = app.get_webview_window("main") {
                let handle_blur = app.handle().clone();
                main_win.on_window_event(move |event| {
                    match event {
                        tauri::WindowEvent::Focused(false) => {
                            if let Some(state) = handle_blur.try_state::<AppState>() {
                                let pinned = *state.is_pinned.lock().unwrap();
                                if pinned {
                                    return;
                                }
                                // Check if dialog is open
                                if *state.dialog_open.lock().unwrap() {
                                    log::info!("MAIN: blur suppressed (dialog open)");
                                    return;
                                }
                                // Check cooldown (resize/interaction in progress)
                                if let Some(cooldown) = *state.hide_cooldown.lock().unwrap() {
                                    if cooldown.elapsed() < std::time::Duration::from_millis(500) {
                                        log::info!("MAIN: blur suppressed (cooldown active)");
                                        return;
                                    }
                                }
                            }
                            // Delay hide by 300ms to allow menu clicks, drag, etc.
                            let h = handle_blur.clone();
                            std::thread::spawn(move || {
                                std::thread::sleep(std::time::Duration::from_millis(300));
                                // Re-check: if window regained focus, don't hide
                                if let Some(main_win) = h.get_webview_window("main") {
                                    if main_win.is_focused().unwrap_or(false) {
                                        return;
                                    }
                                    if let Some(state) = h.try_state::<AppState>() {
                                        if *state.is_pinned.lock().unwrap() {
                                            return;
                                        }
                                    }
                                }
                                log::info!("MAIN: lost focus, not pinned -> hiding (delayed)");
                                do_hide_panel(&h);
                            });
                        }
                        tauri::WindowEvent::Resized(_) => {
                            // Set cooldown to prevent blur-hide during resize
                            if let Some(state) = handle_blur.try_state::<AppState>() {
                                *state.hide_cooldown.lock().unwrap() = Some(std::time::Instant::now());
                            }
                        }
                        tauri::WindowEvent::Moved(_) => {
                            // Set cooldown during drag
                            if let Some(state) = handle_blur.try_state::<AppState>() {
                                *state.hide_cooldown.lock().unwrap() = Some(std::time::Instant::now());
                            }
                        }
                        _ => {}
                    }
                });
            }

            // Register default hotkey: CmdOrCtrl+`
            log::info!("SETUP: registering global shortcut CmdOrCtrl+`...");
            use tauri_plugin_global_shortcut::GlobalShortcutExt;
            if let Err(e) = app.global_shortcut().register("CmdOrCtrl+`") {
                log::warn!("SETUP: Failed to register global shortcut: {e}");
            } else {
                log::info!("SETUP: global shortcut registered");
            }

            // Drag-drop on both windows handled via the global drag-drop event listener
            let h_drop = app.handle().clone();
            app.listen("tauri://drag-drop", move |event| {
                // Parse the payload for paths
                if let Ok(payload) = serde_json::from_str::<serde_json::Value>(event.payload()) {
                    if let Some(paths) = payload.get("paths").and_then(|p| p.as_array()) {
                        let path_bufs: Vec<std::path::PathBuf> = paths.iter()
                            .filter_map(|p| p.as_str().map(std::path::PathBuf::from))
                            .collect();
                        if !path_bufs.is_empty() {
                            log::info!("DROP: {} paths received", path_bufs.len());
                            handle_dropped_paths(&path_bufs, &h_drop);
                        }
                    }
                }
            });

            log::info!("SETUP: complete");
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
            pick_folder,
            parse_visible_text,
            get_settings,
            save_app_settings,
            toggle_panel,
            show_panel,
            hide_panel,
            set_pinned,
            get_pinned,
            shrink_notch,
            expand_notch,
            quit_shelly,
            position_panel_center,
            set_auto_start_cmd,
            detect_ide_projects,
            get_available_shells_cmd,
            get_default_shell,
            set_default_shell,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
