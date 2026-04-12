mod auto_start;
mod display_info;
mod ide_detector;
mod pty;
mod session_store;
mod settings;
mod shell_detect;
mod sleep_prevention;
mod sound;
mod status_parser;
mod tray;
mod util;

use std::sync::Mutex;
use tauri::{AppHandle, Emitter, Listener, Manager, State};
use uuid::Uuid;

use pty::PtyManager;
use session_store::{SessionStore, TerminalSession};
use shell_detect::{ShellInfo, detect_default_shell, get_available_shells};
use status_parser::StatusParser;
use util::safe_lock;

struct AppState {
    pty_manager: PtyManager,
    session_store: SessionStore,
    status_parser: StatusParser,
    settings: Mutex<settings::AppSettings>,
    default_shell: Mutex<String>,
    is_pinned: Mutex<bool>,
    hide_cooldown: Mutex<Option<std::time::Instant>>,
    dialog_open: Mutex<bool>,
    has_shown_once: Mutex<bool>,
    animating: Mutex<bool>,
    panel_size: Mutex<(f64, f64)>,
    display_info: Mutex<display_info::DisplayInfo>,
}

const PANEL_W: f64 = 720.0;
const PANEL_H: f64 = 400.0;

fn get_screen_width(app: &AppHandle) -> f64 {
    if let Ok(Some(monitor)) = app.primary_monitor() {
        monitor.size().width as f64 / monitor.scale_factor()
    } else {
        1920.0
    }
}

fn center_x(sw: f64, w: f64) -> i32 {
    ((sw - w) / 2.0) as i32
}

fn is_animating(app: &AppHandle) -> bool {
    app.try_state::<AppState>().map(|s| *safe_lock(&s.animating)).unwrap_or(false)
}

fn set_animating(app: &AppHandle, val: bool) {
    if let Some(state) = app.try_state::<AppState>() {
        *safe_lock(&state.animating) = val;
    }
}

fn get_panel_size(app: &AppHandle) -> (f64, f64) {
    app.try_state::<AppState>()
        .map(|s| *safe_lock(&s.panel_size))
        .unwrap_or((PANEL_W, PANEL_H))
}

fn get_top_inset(app: &AppHandle) -> f64 {
    app.try_state::<AppState>()
        .map(|s| safe_lock(&s.display_info).top_inset)
        .unwrap_or(0.0)
}

fn do_show_panel(app: &AppHandle) {
    if let Some(main_win) = app.get_webview_window("main") {
        if main_win.is_visible().unwrap_or(false) { return; }
        if is_animating(app) { return; }
        set_animating(app, true);

        let sw = get_screen_width(app);
        let (target_w, target_h) = get_panel_size(app);
        let first_show = if let Some(state) = app.try_state::<AppState>() {
            let mut flag = safe_lock(&state.has_shown_once);
            if !*flag { *flag = true; true } else { false }
        } else { false };

        // Start from pill-like shape: small, centered at top
        let start_w = 140.0_f64;
        let start_h = 38.0_f64;
        let start_y: f64 = get_top_inset(app);

        // Hide notch so it doesn't overlap with panel animation
        if let Some(notch) = app.get_webview_window("notch") {
            let _ = notch.hide();
        }

        // Position offscreen BEFORE showing — no flash
        let _ = main_win.set_position(tauri::LogicalPosition::new(center_x(sw, start_w), -400));
        let _ = main_win.set_size(tauri::LogicalSize::new(start_w, start_h));

        // Tell frontend to use pill-like rounding, and signal visibility immediately
        let _ = app.emit("panel-animating", true);
        let _ = app.emit("panel-visibility", true);

        let handle = app.clone();
        std::thread::spawn(move || {
            if first_show {
                std::thread::sleep(std::time::Duration::from_millis(200));
            }
            // Move to animation start position, then show
            if let Some(win) = handle.get_webview_window("main") {
                let _ = win.set_size(tauri::LogicalSize::new(start_w, start_h));
                let _ = win.set_position(tauri::LogicalPosition::new(center_x(sw, start_w), start_y as i32));
                std::thread::sleep(std::time::Duration::from_millis(10));
                let _ = win.show();
                let _ = win.set_focus();
            }

            let steps: u64 = 18;
            let total_ms: u64 = 280;

            for i in 1..=steps {
                let t = i as f64 / steps as f64;
                // Spring ease-out with overshoot
                let ease = if t < 0.8 {
                    let t2 = t / 0.8;
                    1.0 - (1.0 - t2).powi(3)
                } else {
                    let t2 = (t - 0.8) / 0.2;
                    1.0 + 0.02 * (1.0 - t2) * (std::f64::consts::PI * t2).sin()
                };
                let clamped = ease.min(1.0);

                let w = start_w + (target_w - start_w) * clamped;
                let h = start_h + (target_h - start_h) * clamped;
                let y = start_y;

                if let Some(win) = handle.get_webview_window("main") {
                    let _ = win.set_size(tauri::LogicalSize::new(w, h));
                    let _ = win.set_position(tauri::LogicalPosition::new(center_x(sw, w), y as i32));
                }
                std::thread::sleep(std::time::Duration::from_millis(total_ms / steps));
            }
            // Snap to final
            if let Some(win) = handle.get_webview_window("main") {
                let _ = win.set_size(tauri::LogicalSize::new(target_w, target_h));
                let _ = win.set_position(tauri::LogicalPosition::new(center_x(sw, target_w), start_y as i32));
            }
            let _ = handle.emit("panel-animating", false);
            set_animating(&handle, false);
        });
        log::info!("do_show_panel: growing from pill");
    }
}

fn do_hide_panel(app: &AppHandle) {
    if let Some(main_win) = app.get_webview_window("main") {
        if !main_win.is_visible().unwrap_or(false) { return; }
        if is_animating(app) { return; }
        set_animating(app, true);

        let (target_w, target_h) = get_panel_size(app);
        let _ = app.emit("panel-animating", true);

        let handle = app.clone();
        std::thread::spawn(move || {
            let top_y = get_top_inset(&handle);
            let steps: u64 = 12;
            let total_ms: u64 = 180;
            let sw = get_screen_width(&handle);
            // Shrink back toward pill shape
            let end_w = 140.0_f64;
            let end_h = 38.0_f64;

            for i in 1..=steps {
                let t = i as f64 / steps as f64;
                // Ease-in quadratic
                let ease = t * t;

                let w = target_w + (end_w - target_w) * ease;
                let h = target_h + (end_h - target_h) * ease;

                if let Some(win) = handle.get_webview_window("main") {
                    let _ = win.set_size(tauri::LogicalSize::new(w, h));
                    let _ = win.set_position(tauri::LogicalPosition::new(center_x(sw, w), top_y as i32));
                }
                std::thread::sleep(std::time::Duration::from_millis(total_ms / steps));
            }
            // Hide panel, then show notch back
            if let Some(win) = handle.get_webview_window("main") {
                let _ = win.hide();
                let _ = handle.emit("panel-visibility", false);
                let _ = handle.emit("panel-animating", false);
                std::thread::sleep(std::time::Duration::from_millis(20));
                let _ = win.set_size(tauri::LogicalSize::new(target_w, target_h));
            }
            // Restore notch after panel is fully hidden
            if let Some(notch) = handle.get_webview_window("notch") {
                let _ = notch.show();
            }
            set_animating(&handle, false);
        });
        log::info!("do_hide_panel: shrinking to pill");
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
    let shell = safe_lock(&state.default_shell).clone();
    log::info!("CMD create_terminal: using shell={shell}");
    state.session_store.set_session_started(&session_id);
    state.pty_manager.create(id, &working_dir, &shell, cols, rows, app)
}

/// Send optional startup command after shell prompt appears.
/// The PTY already starts in the correct working directory (via cmd.cwd),
/// so we only send `claude` if auto-launch is enabled and CLAUDE.md exists.
#[tauri::command]
fn send_startup_command(
    session_id: String,
    state: State<'_, AppState>,
) -> Result<(), String> {
    let id = Uuid::parse_str(&session_id).map_err(|e| e.to_string())?;

    let session = state.session_store.get_session(&session_id)
        .ok_or("Session not found")?;

    let wd = &session.working_directory;
    if wd.is_empty() {
        return Ok(());
    }

    let settings = safe_lock(&state.settings).clone();
    let should_launch_claude = settings.auto_launch_claude
        && !session.skip_auto_launch
        && std::path::Path::new(wd).join("CLAUDE.md").exists();

    if should_launch_claude {
        log::info!("CMD send_startup_command: session={session_id}, launching claude");
        state.pty_manager.write_input(id, b"claude --continue\r\n")
    } else {
        log::info!("CMD send_startup_command: session={session_id}, no auto-launch needed");
        Ok(())
    }
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
    maybe_save_sessions(&state);
    session
}

#[tauri::command]
fn select_session(session_id: String, state: State<'_, AppState>) {
    log::info!("CMD select_session: {session_id}");
    state.session_store.select_session(&session_id);
    maybe_save_sessions(&state);
}

#[tauri::command]
fn remove_session(session_id: String, state: State<'_, AppState>) -> Option<String> {
    log::info!("CMD remove_session: {session_id}");
    if let Ok(id) = Uuid::parse_str(&session_id) {
        state.pty_manager.destroy(id);
    }
    let result = state.session_store.remove_session(&session_id);
    maybe_save_sessions(&state);
    result
}

#[tauri::command]
fn rename_session(session_id: String, name: String, state: State<'_, AppState>) {
    state.session_store.rename_session(&session_id, &name);
    maybe_save_sessions(&state);
}

#[tauri::command]
fn get_session(session_id: String, state: State<'_, AppState>) -> Option<TerminalSession> {
    state.session_store.get_session(&session_id)
}

/// Persist sessions to disk if rememberSessions is enabled
fn maybe_save_sessions(state: &AppState) {
    let settings = safe_lock(&state.settings);
    if settings.remember_sessions {
        let sessions = state.session_store.get_sessions();
        settings::save_sessions(&sessions);
    }
}

// --- File/folder commands ---

#[tauri::command]
async fn pick_folder(app: AppHandle, state: State<'_, AppState>) -> Result<Option<session_store::TerminalSession>, String> {
    use tauri_plugin_dialog::DialogExt;

    // Suppress blur-hide while dialog is open
    *safe_lock(&state.dialog_open) = true;

    // Temporarily disable alwaysOnTop so the dialog appears above the panel
    if let Some(main_win) = app.get_webview_window("main") {
        let _ = main_win.set_always_on_top(false);
    }

    let folder = app.dialog().file()
        .set_title("Select folder for new terminal session")
        .blocking_pick_folder();

    // Restore alwaysOnTop
    if let Some(main_win) = app.get_webview_window("main") {
        let _ = main_win.set_always_on_top(true);
    }

    *safe_lock(&state.dialog_open) = false;

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
    // Wrap in catch_unwind so a panic in status parsing can never kill the process
    let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        state.status_parser.parse_visible_text(&session_id, &text, &state.session_store, &app);
        // Check for pending sound notifications (1.5s confirmation)
        state.status_parser.check_pending_sounds(&state.session_store);
    }));
}

// --- Settings commands ---

#[tauri::command]
fn get_settings(state: State<'_, AppState>) -> settings::AppSettings {
    safe_lock(&state.settings).clone()
}

#[tauri::command]
fn save_app_settings(new_settings: settings::AppSettings, state: State<'_, AppState>) {
    let old_remember = safe_lock(&state.settings).remember_sessions;
    settings::save_settings(&new_settings);
    // Apply default shell change
    *safe_lock(&state.default_shell) = new_settings.default_shell.clone();
    // If rememberSessions was toggled off, delete saved sessions
    if old_remember && !new_settings.remember_sessions {
        settings::save_sessions(&[]);
    }
    // If toggled on, save current sessions immediately
    if !old_remember && new_settings.remember_sessions {
        let sessions = state.session_store.get_sessions();
        settings::save_sessions(&sessions);
    }
    *safe_lock(&state.settings) = new_settings;
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
    *safe_lock(&state.is_pinned) = pinned;
    // Set cooldown so any in-flight blur thread won't hide the panel
    if pinned {
        *safe_lock(&state.hide_cooldown) = Some(std::time::Instant::now());
    }
}

#[tauri::command]
fn get_pinned(state: State<'_, AppState>) -> bool {
    *safe_lock(&state.is_pinned)
}

#[tauri::command]
fn quit_shelly(app: AppHandle) {
    log::info!("CMD quit_app");
    app.exit(0);
}

#[tauri::command]
fn get_hotkey(state: State<'_, AppState>) -> String {
    safe_lock(&state.settings).hotkey.clone()
}

#[tauri::command]
fn set_hotkey(accelerator: String, state: State<'_, AppState>, app: AppHandle) -> Result<(), String> {
    use tauri_plugin_global_shortcut::GlobalShortcutExt;
    let trimmed = accelerator.trim().to_string();
    if trimmed.is_empty() {
        return Err("Empty accelerator".to_string());
    }
    let gs = app.global_shortcut();
    // Unregister previous first
    let prev = safe_lock(&state.settings).hotkey.clone();
    let _ = gs.unregister(prev.as_str());
    // Register new
    if let Err(e) = gs.register(trimmed.as_str()) {
        // Re-register previous so user isn't left with no hotkey
        let _ = gs.register(prev.as_str());
        return Err(format!("Failed to register {trimmed}: {e}"));
    }
    // Persist
    let mut s = safe_lock(&state.settings);
    s.hotkey = trimmed;
    settings::save_settings(&s);
    Ok(())
}

#[tauri::command]
fn shrink_notch(app: AppHandle) {
    log::info!("CMD shrink_notch");
    if let Some(notch) = app.get_webview_window("notch") {
        // Collapsed pill: 84x38
        let w = 84.0;
        let h = 38.0;
        let _ = notch.set_size(tauri::LogicalSize::new(w, h));
        if let Ok(Some(monitor)) = app.primary_monitor() {
            let sw = monitor.size().width as f64 / monitor.scale_factor();
            let x = ((sw - w) / 2.0) as i32;
            let _ = notch.set_position(tauri::LogicalPosition::new(x, get_top_inset(&app) as i32));
        }
    }
}

#[tauri::command]
fn expand_notch(app: AppHandle) {
    log::info!("CMD expand_notch");
    if let Some(notch) = app.get_webview_window("notch") {
        // Hover state: slightly wider for hover feedback
        let w = 100.0;
        let h = 38.0;
        let _ = notch.set_size(tauri::LogicalSize::new(w, h));
        if let Ok(Some(monitor)) = app.primary_monitor() {
            let sw = monitor.size().width as f64 / monitor.scale_factor();
            let x = ((sw - w) / 2.0) as i32;
            let _ = notch.set_position(tauri::LogicalPosition::new(x, get_top_inset(&app) as i32));
        }
    }
}

#[tauri::command]
fn position_panel_center(app: AppHandle) {
    if let Some(main_win) = app.get_webview_window("main") {
        let sw = get_screen_width(&app);
        let (w, _) = get_panel_size(&app);
        let x = center_x(sw, w);
        let _ = main_win.set_position(tauri::LogicalPosition::new(x, get_top_inset(&app) as i32));
        log::info!("CMD position_panel_center: x={x}");
    }
}

#[tauri::command]
fn get_display_info(state: State<'_, AppState>) -> display_info::DisplayInfo {
    safe_lock(&state.display_info).clone()
}

#[tauri::command]
fn mark_session_interacted(session_id: String, state: State<'_, AppState>, app: AppHandle) {
    if state.session_store.mark_interacted(&session_id) {
        let _ = app.emit(
            "status-changed",
            serde_json::json!({
                "sessionId": session_id,
                "status": session_store::TerminalStatus::Idle,
            }),
        );
        let _ = app.emit("sessions-updated", &state.session_store.get_sessions());
    }
}

#[tauri::command]
fn get_attention_settings(state: State<'_, AppState>) -> settings::AttentionSettings {
    util::safe_lock(&state.settings).attention.clone()
}

#[tauri::command]
fn set_attention_settings(
    new_attention: settings::AttentionSettings,
    state: State<'_, AppState>,
) {
    let normalized = new_attention.normalized();
    {
        let mut s = util::safe_lock(&state.settings);
        s.attention = normalized;
        settings::save_settings(&s);
    }
}

// --- Shell commands ---

#[tauri::command]
fn get_available_shells_cmd() -> Vec<ShellInfo> {
    get_available_shells()
}

#[tauri::command]
fn get_default_shell(state: State<'_, AppState>) -> String {
    safe_lock(&state.default_shell).clone()
}

#[tauri::command]
fn set_default_shell(path: String, state: State<'_, AppState>) {
    *safe_lock(&state.default_shell) = path;
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    env_logger::init();

    let app_settings = settings::load_settings();
    let default_shell = app_settings.default_shell.clone();
    log::info!("Default shell: {default_shell}");

    let session_store = SessionStore::new();

    // Restore saved sessions if rememberSessions is enabled
    if app_settings.remember_sessions {
        let saved = settings::load_sessions();
        if !saved.is_empty() {
            log::info!("Restoring {} saved sessions", saved.len());
            session_store.restore_sessions(saved);
        }
    }
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
            has_shown_once: Mutex::new(false),
            animating: Mutex::new(false),
            panel_size: Mutex::new((PANEL_W, PANEL_H)),
            display_info: Mutex::new(display_info::DisplayInfo::default()),
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
            do_show_panel(app);
        }))
        .setup(|app| {
            log::info!("SETUP: initializing tray...");
            tray::setup_tray(app.handle())?;
            log::info!("SETUP: tray initialized");

            // Detect display (notch / top inset)
            let info = display_info::detect();
            log::info!("SETUP: display_info = {info:?}");
            if let Some(state) = app.try_state::<AppState>() {
                *safe_lock(&state.display_info) = info;
            }

            // Position notch window at top-center of screen (greeting size: 260x46)
            if let Some(notch) = app.get_webview_window("notch") {
                log::info!("SETUP: positioning notch window...");
                let handle = app.handle().clone();
                tauri::async_runtime::spawn(async move {
                    if let Some(monitor) = handle.primary_monitor().ok().flatten() {
                        let screen_width = monitor.size().width as f64 / monitor.scale_factor();
                        let x = ((screen_width - 260.0) / 2.0) as i32;
                        let _ = notch.set_position(tauri::LogicalPosition::new(x, get_top_inset(&handle) as i32));
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
                                let pinned = *safe_lock(&state.is_pinned);
                                if pinned {
                                    return;
                                }
                                // Suppress during animation
                                if *safe_lock(&state.animating) {
                                    log::info!("MAIN: blur suppressed (animating)");
                                    return;
                                }
                                // Check if dialog is open
                                if *safe_lock(&state.dialog_open) {
                                    log::info!("MAIN: blur suppressed (dialog open)");
                                    return;
                                }
                                // Check cooldown (resize/interaction in progress)
                                if let Some(cooldown) = *safe_lock(&state.hide_cooldown) {
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
                                        if *safe_lock(&state.is_pinned) {
                                            return;
                                        }
                                    }
                                }
                                log::info!("MAIN: lost focus, not pinned -> hiding (delayed)");
                                do_hide_panel(&h);
                            });
                        }
                        tauri::WindowEvent::Resized(size) => {
                            // Set cooldown to prevent blur-hide during resize
                            if let Some(state) = handle_blur.try_state::<AppState>() {
                                *safe_lock(&state.hide_cooldown) = Some(std::time::Instant::now());
                                // Track user-resized dimensions (skip during animation)
                                if !*safe_lock(&state.animating) {
                                    if let Some(win) = handle_blur.get_webview_window("main") {
                                        let scale = win.scale_factor().unwrap_or(1.0);
                                        let w = size.width as f64 / scale;
                                        let h = size.height as f64 / scale;
                                        if w > 100.0 && h > 50.0 {
                                            *safe_lock(&state.panel_size) = (w, h);
                                        }
                                    }
                                }
                            }
                        }
                        tauri::WindowEvent::Moved(_) => {
                            // Set cooldown during drag
                            if let Some(state) = handle_blur.try_state::<AppState>() {
                                *safe_lock(&state.hide_cooldown) = Some(std::time::Instant::now());
                            }
                        }
                        _ => {}
                    }
                });
            }

            // Pre-warm main window so first user-triggered show finds a hot webview.
            if let Some(main_win) = app.get_webview_window("main") {
                let (target_w, target_h) = (PANEL_W, PANEL_H);
                tauri::async_runtime::spawn(async move {
                    // Offscreen position far from the visible area
                    let _ = main_win.set_size(tauri::LogicalSize::new(target_w, target_h));
                    let _ = main_win.set_position(tauri::LogicalPosition::new(-4000, -4000));
                    let _ = main_win.show();
                    std::thread::sleep(std::time::Duration::from_millis(200));
                    let _ = main_win.hide();
                    log::info!("SETUP: main window pre-warmed");
                });
            }

            // Register trigger hotkey from settings
            use tauri_plugin_global_shortcut::GlobalShortcutExt;
            let hotkey_str = app.try_state::<AppState>()
                .map(|s| safe_lock(&s.settings).hotkey.clone())
                .unwrap_or_else(|| "CmdOrCtrl+`".to_string());
            log::info!("SETUP: registering global shortcut {hotkey_str}");
            if let Err(e) = app.global_shortcut().register(hotkey_str.as_str()) {
                log::warn!("SETUP: failed to register {hotkey_str}: {e}");
                // Fall back to default
                let _ = app.global_shortcut().register("CmdOrCtrl+`");
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

            // Log display scale for debugging
            if let Some(main_win) = app.get_webview_window("main") {
                let scale = main_win.scale_factor().unwrap_or(1.0);
                log::info!("SETUP: display scale factor = {scale}");
            }

            log::info!("SETUP: complete");
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            create_terminal,
            send_startup_command,
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
            get_hotkey,
            set_hotkey,
            get_display_info,
            mark_session_interacted,
            get_attention_settings,
            set_attention_settings,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
