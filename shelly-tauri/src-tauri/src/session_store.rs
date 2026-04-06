use serde::{Deserialize, Serialize};
use std::sync::Mutex;
use uuid::Uuid;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub enum TerminalStatus {
    Idle,
    Working,
    WaitingForInput,
    TaskCompleted,
    Interrupted,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TerminalSession {
    pub id: String,
    pub project_name: String,
    pub project_path: Option<String>,
    pub working_directory: String,
    pub has_started: bool,
    pub status: TerminalStatus,
    pub is_active: bool,
    pub skip_auto_launch: bool,
}

pub struct SessionStore {
    sessions: Mutex<Vec<TerminalSession>>,
    active_session_id: Mutex<Option<String>>,
}

impl SessionStore {
    pub fn new() -> Self {
        Self {
            sessions: Mutex::new(Vec::new()),
            active_session_id: Mutex::new(None),
        }
    }

    pub fn get_sessions(&self) -> Vec<TerminalSession> {
        self.sessions.lock().unwrap().clone()
    }

    pub fn get_active_session_id(&self) -> Option<String> {
        self.active_session_id.lock().unwrap().clone()
    }

    pub fn add_session(
        &self,
        name: Option<String>,
        project_path: Option<String>,
        working_dir: Option<String>,
    ) -> TerminalSession {
        let home = dirs::home_dir()
            .map(|p| p.to_string_lossy().into_owned())
            .unwrap_or_default();
        let wd = working_dir
            .or_else(|| project_path.clone())
            .unwrap_or(home);

        let session = TerminalSession {
            id: Uuid::new_v4().to_string(),
            project_name: name.unwrap_or_else(|| "Terminal".into()),
            project_path,
            working_directory: wd,
            has_started: false,
            status: TerminalStatus::Idle,
            is_active: false,
            skip_auto_launch: false,
        };

        let mut sessions = self.sessions.lock().unwrap();
        sessions.push(session.clone());

        let mut active = self.active_session_id.lock().unwrap();
        if active.is_none() {
            *active = Some(session.id.clone());
            drop(active);
            drop(sessions);
            self.update_is_active(&session.id);
        }

        session
    }

    pub fn select_session(&self, session_id: &str) {
        *self.active_session_id.lock().unwrap() = Some(session_id.to_string());
        self.update_is_active(session_id);
    }

    pub fn remove_session(&self, session_id: &str) -> Option<String> {
        let mut sessions = self.sessions.lock().unwrap();
        sessions.retain(|s| s.id != session_id);

        let mut active = self.active_session_id.lock().unwrap();
        if active.as_deref() == Some(session_id) {
            *active = sessions.first().map(|s| s.id.clone());
            let new_active = active.clone();
            drop(active);
            drop(sessions);
            if let Some(ref id) = new_active {
                self.update_is_active(id);
            }
            return new_active;
        }
        active.clone()
    }

    pub fn rename_session(&self, session_id: &str, name: &str) {
        let mut sessions = self.sessions.lock().unwrap();
        if let Some(s) = sessions.iter_mut().find(|s| s.id == session_id) {
            s.project_name = name.to_string();
        }
    }

    pub fn set_session_started(&self, session_id: &str) {
        let mut sessions = self.sessions.lock().unwrap();
        if let Some(s) = sessions.iter_mut().find(|s| s.id == session_id) {
            s.has_started = true;
        }
    }

    pub fn update_status(&self, session_id: &str, status: TerminalStatus) {
        let mut sessions = self.sessions.lock().unwrap();
        if let Some(s) = sessions.iter_mut().find(|s| s.id == session_id) {
            s.status = status;
        }
    }

    pub fn get_session(&self, session_id: &str) -> Option<TerminalSession> {
        let sessions = self.sessions.lock().unwrap();
        sessions.iter().find(|s| s.id == session_id).cloned()
    }

    pub fn ensure_default_session(&self) {
        let sessions = self.sessions.lock().unwrap();
        if sessions.is_empty() {
            drop(sessions);
            self.add_session(None, None, None);
        }
    }

    /// Load saved sessions from disk, marking all as not-started (terminals need recreation).
    /// Only the previously active session will auto-launch claude; others get skip_auto_launch=true.
    pub fn restore_sessions(&self, saved: Vec<TerminalSession>) {
        if saved.is_empty() {
            return;
        }
        let mut sessions = self.sessions.lock().unwrap();
        sessions.clear();
        let mut active_id = None;
        for mut s in saved {
            s.has_started = false;
            s.status = TerminalStatus::Idle;
            // Only the previously active session should auto-launch claude
            if s.is_active {
                active_id = Some(s.id.clone());
                s.skip_auto_launch = false;
            } else {
                s.skip_auto_launch = true;
            }
            sessions.push(s);
        }
        drop(sessions);
        if let Some(id) = active_id {
            *self.active_session_id.lock().unwrap() = Some(id.clone());
            self.update_is_active(&id);
        } else {
            // Fallback: select first
            let sessions = self.sessions.lock().unwrap();
            if let Some(first) = sessions.first() {
                let id = first.id.clone();
                drop(sessions);
                *self.active_session_id.lock().unwrap() = Some(id.clone());
                self.update_is_active(&id);
            }
        }
    }

    fn update_is_active(&self, active_id: &str) {
        let mut sessions = self.sessions.lock().unwrap();
        for s in sessions.iter_mut() {
            s.is_active = s.id == active_id;
        }
    }
}
