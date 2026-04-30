use regex::Regex;
use std::collections::HashMap;
use std::sync::Mutex;
use std::time::{Duration, Instant};
use crate::session_store::{SessionStore, TerminalStatus};
use crate::sound;
use crate::util::safe_lock;
use tauri::{AppHandle, Emitter};

pub struct StatusParser {
    completion_pattern: Regex,
    last_working_time: Mutex<HashMap<String, Instant>>,
    /// Pending sound confirmations: session_id -> (status, when_set)
    pending_sounds: Mutex<HashMap<String, (TerminalStatus, Instant)>>,
    /// Confidence gate for noisy polling transitions.
    /// session_id -> (candidate_status, consecutive_hits)
    pending_status_confidence: Mutex<HashMap<String, (TerminalStatus, u8)>>,
}

impl StatusParser {
    pub fn new() -> Self {
        Self {
            completion_pattern: Regex::new(r"[✢✳✶✻✽].*\bfor\b.*\d+[ms]").unwrap(),
            last_working_time: Mutex::new(HashMap::new()),
            pending_sounds: Mutex::new(HashMap::new()),
            pending_status_confidence: Mutex::new(HashMap::new()),
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

        // Completion message from Working state (only when Claude is actually running)
        if session.claude_running
            && session.status == TerminalStatus::Working
            && self.completion_pattern.is_match(&text)
        {
            log::info!("StatusParser: completion detected in raw output");
            self.set_status(session_id, TerminalStatus::TaskCompleted, store, app);
            return;
        }

        // Start of working from Idle (only when Claude is actually running)
        if session.claude_running && session.status == TerminalStatus::Idle {
            let lower = text.to_lowercase();
            if lower.contains("esc to interrupt")
                || lower.contains("clauding")
                || lower.contains("thinking with")
            {
                self.set_status(session_id, TerminalStatus::Working, store, app);
                return;
            }
        }

        // WaitingForInput — parse_visible_text only runs for the active tab, so
        // background sessions must detect this here from raw output.
        // Restrict to Working→WaitingForInput only (mirrors the update_status guard that
        // blocks Idle→WaitingForInput to avoid false-positives from leftover session text).
        if session.claude_running && session.status == TerminalStatus::Working {
            let lower = text.to_lowercase();
            if lower.contains("esc to cancel")
                || lower.contains("do you want to proceed")
                || lower.contains("would you like to proceed")
                || lower.contains("yes / no")
                || lower.contains("(y)es/(n)o")
                || lower.contains("keep planning")
                || lower.contains("auto-accept edits")
            {
                self.set_status(session_id, TerminalStatus::WaitingForInput, store, app);
                return;
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

        let mut new_status = self.classify_visible_text(visible_text);
        new_status = self.apply_runtime_gate(new_status, session.claude_running);
        new_status = self.apply_confidence_gate(session_id, new_status, &session.status);
        self.update_status(session_id, new_status, &session.status, store, app);
    }

    fn apply_runtime_gate(&self, status: TerminalStatus, claude_running: bool) -> TerminalStatus {
        if claude_running {
            return status;
        }
        match status {
            TerminalStatus::Working
            | TerminalStatus::WaitingForInput
            | TerminalStatus::TaskCompleted => TerminalStatus::Idle,
            _ => status,
        }
    }

    fn apply_confidence_gate(
        &self,
        session_id: &str,
        candidate: TerminalStatus,
        old_status: &TerminalStatus,
    ) -> TerminalStatus {
        // Only gate noisy transitions. Working and Interrupted should remain responsive.
        if !matches!(
            candidate,
            TerminalStatus::WaitingForInput | TerminalStatus::TaskCompleted
        ) {
            safe_lock(&self.pending_status_confidence).remove(session_id);
            return candidate;
        }

        if candidate == *old_status {
            safe_lock(&self.pending_status_confidence).remove(session_id);
            return candidate;
        }

        let mut pending = safe_lock(&self.pending_status_confidence);
        let entry = pending
            .entry(session_id.to_string())
            .or_insert((candidate.clone(), 0));

        if entry.0 == candidate {
            entry.1 = entry.1.saturating_add(1);
        } else {
            *entry = (candidate.clone(), 1);
        }

        // Need 2 consecutive polls before committing.
        if entry.1 >= 2 {
            pending.remove(session_id);
            candidate
        } else {
            old_status.clone()
        }
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
            if trimmed.starts_with('❯') || trimmed.starts_with('?') {
                // Skip past the first character using char_indices to get a valid byte boundary
                // ('❯' is 3 bytes in UTF-8, so trimmed[1..] would panic)
                if let Some((idx, _)) = trimmed.char_indices().nth(1) {
                    if trimmed[idx..].trim_start().chars().any(|c| c.is_ascii_digit()) {
                        return TerminalStatus::WaitingForInput;
                    }
                }
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
            return TerminalStatus::TaskCompleted;
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

        // Sticky Working (4s)
        if *old_status == TerminalStatus::Working && new_status == TerminalStatus::Idle {
            let times = safe_lock(&self.last_working_time);
            if let Some(last) = times.get(session_id) {
                if last.elapsed() < Duration::from_secs(4) {
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
            safe_lock(&self.last_working_time)
                .insert(session_id.to_string(), Instant::now());
        }

        // Schedule sound for background sessions (1.5s confirmation delay)
        match status {
            TerminalStatus::TaskCompleted | TerminalStatus::WaitingForInput => {
                // Only for non-active sessions
                if let Some(session) = store.get_session(session_id) {
                    if !session.is_active {
                        safe_lock(&self.pending_sounds).insert(
                            session_id.to_string(),
                            (status.clone(), Instant::now()),
                        );
                    }
                }
            }
            _ => {
                // Cancel pending sound if status changed away
                safe_lock(&self.pending_sounds).remove(session_id);
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

        // Attention-required: pop panel if enabled & status is a configured trigger,
        // and we haven't already popped for this status instance.
        if matches!(
            status,
            TerminalStatus::TaskCompleted
                | TerminalStatus::WaitingForInput
                | TerminalStatus::Interrupted
        ) {
            use tauri::Manager;
            if let Some(state) = app.try_state::<crate::AppState>() {
                let settings = crate::util::safe_lock(&state.settings).clone();
                if settings.attention.enabled
                    && settings.attention.trigger_statuses.contains(&status)
                    && store.try_mark_popped(session_id)
                {
                    let _ = app.emit(
                        "attention-required",
                        serde_json::json!({
                            "sessionId": session_id,
                            "status": status,
                        }),
                    );
                }
            }
        }
    }

    /// Check and fire pending sounds (call this periodically, e.g. from status polling)
    pub fn check_pending_sounds(&self, store: &SessionStore) {
        let mut pending = safe_lock(&self.pending_sounds);
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
