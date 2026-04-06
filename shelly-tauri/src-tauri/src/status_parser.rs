use regex::Regex;
use std::collections::HashMap;
use std::sync::Mutex;
use std::time::{Duration, Instant};
use crate::session_store::{SessionStore, TerminalStatus};
use crate::sound;
use tauri::{AppHandle, Emitter};

pub struct StatusParser {
    completion_pattern: Regex,
    last_working_time: Mutex<HashMap<String, Instant>>,
    /// Pending sound confirmations: session_id -> (status, when_set)
    pending_sounds: Mutex<HashMap<String, (TerminalStatus, Instant)>>,
}

impl StatusParser {
    pub fn new() -> Self {
        Self {
            completion_pattern: Regex::new(r"[✢✳✶✻✽].*\bfor\b.*\d+[ms]").unwrap(),
            last_working_time: Mutex::new(HashMap::new()),
            pending_sounds: Mutex::new(HashMap::new()),
        }
    }

    /// Fast-path: parse raw PTY output bytes
    pub fn parse_raw_output(
        &self,
        session_id: &str,
        data: &[u8],
        store: &SessionStore,
        app: &AppHandle,
    ) {
        let text = String::from_utf8_lossy(data);
        let session = match store.get_session(session_id) {
            Some(s) => s,
            None => return,
        };

        // Completion message from Working state
        if session.status == TerminalStatus::Working && self.completion_pattern.is_match(&text) {
            log::info!("StatusParser: completion detected in raw output");
            self.set_status(session_id, TerminalStatus::TaskCompleted, store, app);
            return;
        }

        // Start of working from Idle
        if session.status == TerminalStatus::Idle {
            let lower = text.to_lowercase();
            if lower.contains("esc to interrupt")
                || lower.contains("clauding")
                || lower.contains("thinking with")
            {
                self.set_status(session_id, TerminalStatus::Working, store, app);
            }
        }
    }

    /// Parse clean visible text from xterm.js buffer
    pub fn parse_visible_text(
        &self,
        session_id: &str,
        visible_text: &str,
        store: &SessionStore,
        app: &AppHandle,
    ) {
        let session = match store.get_session(session_id) {
            Some(s) => s,
            None => return,
        };

        let new_status = self.classify_visible_text(visible_text);
        self.update_status(session_id, new_status, &session.status, store, app);
    }

    fn classify_visible_text(&self, text: &str) -> TerminalStatus {
        let lines: Vec<&str> = text
            .split('\n')
            .map(|l| l.trim_end())
            .filter(|l| !l.is_empty())
            .collect();

        if lines.is_empty() {
            return TerminalStatus::Idle;
        }

        let bottom3: Vec<&str> = lines.iter().rev().take(3).copied().collect();
        let bottom3_text = bottom3.join("\n");
        let bottom8: Vec<&str> = lines.iter().rev().take(8).copied().collect();
        let bottom8_text = bottom8.join("\n");

        // Interrupted
        if bottom8_text.to_lowercase().contains("interrupted")
            && !bottom8_text.to_lowercase().contains("esc to interrupt")
        {
            return TerminalStatus::Interrupted;
        }

        // Working (bottom 3 only)
        let b3_lower = bottom3_text.to_lowercase();
        if b3_lower.contains("esc to interrupt")
            || b3_lower.contains("clauding")
            || b3_lower.contains("thinking with")
            || (b3_lower.contains("reading") && b3_lower.contains("file"))
            || (b3_lower.contains("writing") && b3_lower.contains("file"))
        {
            return TerminalStatus::Working;
        }

        // WaitingForInput (bottom 8)
        let b8_lower = bottom8_text.to_lowercase();
        if b8_lower.contains("esc to cancel")
            || b8_lower.contains("do you want to proceed")
            || b8_lower.contains("would you like to proceed")
            || b8_lower.contains("yes / no")
            || b8_lower.contains("(y)es")
            || b8_lower.contains("keep planning")
            || b8_lower.contains("auto-accept edits")
        {
            return TerminalStatus::WaitingForInput;
        }

        // Selector menu
        for line in bottom8.iter() {
            let trimmed = line.trim_start();
            if (trimmed.starts_with('❯') || trimmed.starts_with('?'))
                && trimmed.len() > 1
                && trimmed[1..].trim_start().chars().any(|c| c.is_ascii_digit())
            {
                return TerminalStatus::WaitingForInput;
            }
        }

        // Completion pattern in bottom 5
        let bottom5_text: String = lines
            .iter()
            .rev()
            .take(5)
            .copied()
            .collect::<Vec<_>>()
            .join("\n");
        if self.completion_pattern.is_match(&bottom5_text) {
            return TerminalStatus::Idle;
        }

        TerminalStatus::Idle
    }

    fn update_status(
        &self,
        session_id: &str,
        new_status: TerminalStatus,
        old_status: &TerminalStatus,
        store: &SessionStore,
        app: &AppHandle,
    ) {
        if new_status == *old_status {
            return;
        }

        // Don't let polling clear TaskCompleted
        if *old_status == TerminalStatus::TaskCompleted && new_status == TerminalStatus::Idle {
            return;
        }

        // Don't go Idle -> WaitingForInput
        if *old_status == TerminalStatus::Idle && new_status == TerminalStatus::WaitingForInput {
            return;
        }

        // Sticky Working (2s)
        if *old_status == TerminalStatus::Working && new_status == TerminalStatus::Idle {
            let times = self.last_working_time.lock().unwrap();
            if let Some(last) = times.get(session_id) {
                if last.elapsed() < Duration::from_secs(2) {
                    return;
                }
            }
        }

        self.set_status(session_id, new_status, store, app);
    }

    fn set_status(
        &self,
        session_id: &str,
        status: TerminalStatus,
        store: &SessionStore,
        app: &AppHandle,
    ) {
        if status == TerminalStatus::Working {
            self.last_working_time
                .lock()
                .unwrap()
                .insert(session_id.to_string(), Instant::now());
        }

        // Schedule sound for background sessions (1.5s confirmation delay)
        match status {
            TerminalStatus::TaskCompleted | TerminalStatus::WaitingForInput => {
                // Only for non-active sessions
                if let Some(session) = store.get_session(session_id) {
                    if !session.is_active {
                        self.pending_sounds.lock().unwrap().insert(
                            session_id.to_string(),
                            (status.clone(), Instant::now()),
                        );
                    }
                }
            }
            _ => {
                // Cancel pending sound if status changed away
                self.pending_sounds.lock().unwrap().remove(session_id);
            }
        }

        store.update_status(session_id, status.clone());

        // Emit status-changed for React
        let _ = app.emit(
            "status-changed",
            serde_json::json!({
                "sessionId": session_id,
                "status": status,
            }),
        );

        // Emit sessions-updated for notch
        let sessions = store.get_sessions();
        let _ = app.emit("sessions-updated", &sessions);
    }

    /// Check and fire pending sounds (call this periodically, e.g. from status polling)
    pub fn check_pending_sounds(&self, store: &SessionStore) {
        let mut pending = self.pending_sounds.lock().unwrap();
        let mut to_remove = Vec::new();

        for (session_id, (status, when)) in pending.iter() {
            if when.elapsed() >= Duration::from_millis(1500) {
                // Confirm the session is still in this status
                if let Some(session) = store.get_session(session_id) {
                    if session.status == *status && !session.is_active {
                        match status {
                            TerminalStatus::TaskCompleted => sound::play_task_completed(),
                            TerminalStatus::WaitingForInput => sound::play_waiting_for_input(),
                            _ => {}
                        }
                    }
                }
                to_remove.push(session_id.clone());
            }
        }

        for id in to_remove {
            pending.remove(&id);
        }
    }
}
