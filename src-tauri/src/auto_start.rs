#[cfg(target_os = "windows")]
pub fn set_auto_start(enabled: bool) -> Result<(), String> {
    use winreg::enums::*;
    use winreg::RegKey;
    use std::env;

    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let run_key = hkcu
        .open_subkey_with_flags(
            r"Software\Microsoft\Windows\CurrentVersion\Run",
            KEY_ALL_ACCESS,
        )
        .map_err(|e| e.to_string())?;

    if enabled {
        let exe = env::current_exe().map_err(|e| e.to_string())?;
        run_key
            .set_value("Shelly", &exe.to_string_lossy().to_string())
            .map_err(|e| e.to_string())?;
    } else {
        let _ = run_key.delete_value("Shelly");
    }
    Ok(())
}

#[cfg(target_os = "macos")]
pub fn set_auto_start(enabled: bool) -> Result<(), String> {
    use std::fs;

    let plist_path = dirs::home_dir()
        .ok_or("No home dir")?
        .join("Library/LaunchAgents/com.shelly.app.plist");

    if enabled {
        let exe = std::env::current_exe().map_err(|e| e.to_string())?;
        let plist = format!(
            r#"<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.shelly.app</string>
    <key>ProgramArguments</key>
    <array>
        <string>{}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>"#,
            exe.to_string_lossy()
        );
        if let Some(parent) = plist_path.parent() {
            let _ = fs::create_dir_all(parent);
        }
        fs::write(&plist_path, plist).map_err(|e| e.to_string())?;
    } else {
        let _ = fs::remove_file(&plist_path);
    }
    Ok(())
}
