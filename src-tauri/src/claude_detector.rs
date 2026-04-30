use std::collections::{HashSet, VecDeque};
use std::sync::Mutex;
use std::time::{Duration, Instant};

use crate::util::safe_lock;
use sysinfo::{Pid, ProcessesToUpdate, System};

#[derive(Debug, Clone, Copy, Default)]
pub struct ClaudeRuntime {
    pub running: bool,
    pub claude_pid: Option<u32>,
}

pub struct ClaudeDetector {
    system: Mutex<System>,
    last_refresh_at: Mutex<Option<Instant>>,
}

impl ClaudeDetector {
    pub fn new() -> Self {
        Self {
            system: Mutex::new(System::new_all()),
            last_refresh_at: Mutex::new(None),
        }
    }

    pub fn detect_for_shell_pid(&self, shell_pid: Option<u32>) -> ClaudeRuntime {
        let Some(shell_pid_u32) = shell_pid else {
            return ClaudeRuntime::default();
        };

        self.refresh_if_needed();

        let system = safe_lock(&self.system);
        let descendants = collect_descendant_pids(&system, shell_pid_u32);
        for pid_u32 in descendants {
            if let Some(process) = system.process(Pid::from_u32(pid_u32)) {
                let name = process.name().to_string_lossy().to_ascii_lowercase();
                let exe = process
                    .exe()
                    .map(|p| p.to_string_lossy().to_ascii_lowercase())
                    .unwrap_or_default();
                let cmd = process
                    .cmd()
                    .iter()
                    .map(|p| p.to_string_lossy().to_ascii_lowercase())
                    .collect::<Vec<_>>()
                    .join(" ");
                if is_claude_process(&name, &exe, &cmd) {
                    return ClaudeRuntime {
                        running: true,
                        claude_pid: Some(pid_u32),
                    };
                }
            }
        }

        ClaudeRuntime::default()
    }

    fn refresh_if_needed(&self) {
        let mut last = safe_lock(&self.last_refresh_at);
        if let Some(prev) = *last {
            if prev.elapsed() < Duration::from_millis(500) {
                return;
            }
        }
        let mut system = safe_lock(&self.system);
        system.refresh_processes(ProcessesToUpdate::All, true);
        *last = Some(Instant::now());
    }
}

fn collect_descendant_pids(system: &System, root_pid: u32) -> Vec<u32> {
    let mut out = Vec::new();
    let mut queue = VecDeque::new();
    let mut seen = HashSet::new();
    queue.push_back(root_pid);
    seen.insert(root_pid);

    while let Some(parent) = queue.pop_front() {
        for process in system.processes().values() {
            let is_child = process
                .parent()
                .map(|pid| pid.as_u32() == parent)
                .unwrap_or(false);
            if !is_child {
                continue;
            }
            let pid_u32 = process.pid().as_u32();
            if seen.insert(pid_u32) {
                out.push(pid_u32);
                queue.push_back(pid_u32);
            }
        }
    }

    out
}

fn is_claude_process(name: &str, exe: &str, cmd: &str) -> bool {
    name == "claude"
        || name == "claude.exe"
        || exe.ends_with("/claude")
        || exe.ends_with("\\claude.exe")
        || cmd.contains(" claude ")
        || cmd.starts_with("claude ")
        || cmd.ends_with(" claude")
        || cmd.contains("\\claude.exe")
        || cmd.contains("/claude")
}
