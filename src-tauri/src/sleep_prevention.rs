use std::sync::Mutex;
use crate::util::safe_lock;

pub struct SleepPrevention {
    #[cfg(target_os = "macos")]
    caffeinate: Mutex<Option<std::process::Child>>,
    active: Mutex<bool>,
}

impl SleepPrevention {
    pub fn new() -> Self {
        Self {
            #[cfg(target_os = "macos")]
            caffeinate: Mutex::new(None),
            active: Mutex::new(false),
        }
    }

    pub fn prevent_sleep(&self) {
        let mut active = safe_lock(&self.active);
        if *active {
            return;
        }
        *active = true;
        log::info!("SleepPrevention: preventing sleep");

        #[cfg(target_os = "windows")]
        {
            use windows::Win32::System::Power::{SetThreadExecutionState, ES_CONTINUOUS, ES_DISPLAY_REQUIRED, ES_SYSTEM_REQUIRED};
            unsafe {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            }
        }

        #[cfg(target_os = "macos")]
        {
            let mut child = safe_lock(&self.caffeinate);
            if child.is_none() {
                // -i: prevent idle sleep, -d: prevent display sleep
                *child = std::process::Command::new("caffeinate")
                    .args(["-i", "-d"])
                    .spawn()
                    .ok();
            }
        }
    }

    pub fn allow_sleep(&self) {
        // Also called from Drop to release the OS wake-lock on app exit.
        let mut active = safe_lock(&self.active);
        if !*active {
            return;
        }
        *active = false;
        log::info!("SleepPrevention: allowing sleep");

        #[cfg(target_os = "windows")]
        {
            use windows::Win32::System::Power::{SetThreadExecutionState, ES_CONTINUOUS};
            unsafe {
                SetThreadExecutionState(ES_CONTINUOUS);
            }
        }

        #[cfg(target_os = "macos")]
        {
            let mut child = safe_lock(&self.caffeinate);
            if let Some(mut c) = child.take() {
                let _ = c.kill();
            }
        }
    }
}

impl Drop for SleepPrevention {
    fn drop(&mut self) {
        self.allow_sleep();
    }
}
