use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    AppHandle, Emitter, Manager,
};

pub fn setup_tray(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let new_session = MenuItem::with_id(app, "new_session", "New Session", true, None::<&str>)?;
    let check_updates = MenuItem::with_id(app, "check_updates", "Check for Updates…", true, None::<&str>)?;
    let separator = PredefinedMenuItem::separator(app)?;
    let quit = MenuItem::with_id(app, "quit", "Quit Shelly", true, None::<&str>)?;

    let menu = Menu::with_items(app, &[&new_session, &check_updates, &separator, &quit])?;

    let _tray = TrayIconBuilder::new()
        .icon(app.default_window_icon().unwrap().clone())
        .tooltip("Shelly")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| {
            log::info!("TRAY: menu event: {}", event.id.as_ref());
            match event.id.as_ref() {
                "new_session" => {
                    let _ = app.emit("tray-new-session", ());
                }
                "check_updates" => {
                    crate::do_show_panel(app);
                    let _ = app.emit("tray-check-updates", ());
                }
                "quit" => {
                    app.exit(0);
                }
                _ => {}
            }
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                log::info!("TRAY: left click - toggling panel");
                crate::do_toggle_panel(tray.app_handle());
            }
        })
        .build(app)?;

    Ok(())
}
