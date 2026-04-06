use std::sync::Mutex;
use std::time::Instant;

static LAST_SOUND: Mutex<Option<Instant>> = Mutex::new(None);

/// Play a notification sound for task completion (background sessions only).
/// Throttled to 1 sound per second to prevent rapid repetition.
pub fn play_task_completed() {
    // Throttle: skip if played less than 1s ago
    {
        let mut last = LAST_SOUND.lock().unwrap();
        if let Some(t) = *last {
            if t.elapsed() < std::time::Duration::from_secs(1) {
                return;
            }
        }
        *last = Some(Instant::now());
    }

    std::thread::spawn(|| {
        #[cfg(target_os = "windows")]
        {
            use windows::Win32::System::Diagnostics::Debug::MessageBeep;
            use windows::Win32::UI::WindowsAndMessaging::MB_ICONASTERISK;
            unsafe { let _ = MessageBeep(MB_ICONASTERISK); }
        }

        #[cfg(target_os = "macos")]
        {
            let _ = std::process::Command::new("afplay")
                .arg("/System/Library/Sounds/Glass.aiff")
                .spawn();
        }
    });
}

/// Play a notification sound for waiting-for-input (background sessions only).
pub fn play_waiting_for_input() {
    {
        let mut last = LAST_SOUND.lock().unwrap();
        if let Some(t) = *last {
            if t.elapsed() < std::time::Duration::from_secs(1) {
                return;
            }
        }
        *last = Some(Instant::now());
    }

    std::thread::spawn(|| {
        #[cfg(target_os = "windows")]
        {
            use windows::Win32::System::Diagnostics::Debug::MessageBeep;
            use windows::Win32::UI::WindowsAndMessaging::MB_ICONEXCLAMATION;
            unsafe { let _ = MessageBeep(MB_ICONEXCLAMATION); }
        }

        #[cfg(target_os = "macos")]
        {
            let _ = std::process::Command::new("afplay")
                .arg("/System/Library/Sounds/Tink.aiff")
                .spawn();
        }
    });
}
