use std::collections::{HashSet, VecDeque};
use std::sync::Mutex;
use std::time::{Duration, Instant};

use crate::util::safe_lock;
use sysinfo::{Pid, ProcessesToUpdate, System};

#[derive(Debug, Clone, Default)]
pub struct ClaudeRuntime {
    pub running: bool,
    pub claude_pid: Option<u32>,
    /// The most prominent foreground process running under the shell (e.g. "claude", "node", "bun").
    pub running_process: Option<String>,
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

        let mut best: Option<(u8, String, u32)> = None; // (priority, name, pid)

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

                if let Some((priority, label)) = classify_process(&name, &exe, &cmd) {
                    if best.as_ref().map(|(p, _, _)| priority > *p).unwrap_or(true) {
                        best = Some((priority, label, pid_u32));
                    }
                }
            }
        }

        match best {
            Some((_, ref label, pid)) if label == "claude" => ClaudeRuntime {
                running: true,
                claude_pid: Some(pid),
                running_process: Some("claude".into()),
            },
            Some((_, label, _)) => ClaudeRuntime {
                running: false,
                claude_pid: None,
                running_process: Some(label),
            },
            None => ClaudeRuntime::default(),
        }
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

/// Returns `Some((priority, label))` for recognized foreground processes.
/// Higher priority wins when multiple processes are found in the tree.
fn classify_process(name: &str, exe: &str, cmd: &str) -> Option<(u8, String)> {
    // Claude — highest priority
    if is_claude(name, exe, cmd) {
        return Some((100, "claude".into()));
    }
    // Node.js
    if matches!(name, "node" | "node.exe" | "npm" | "npm.cmd" | "npm.exe" | "yarn" | "yarn.cmd" | "yarn.exe" | "pnpm" | "pnpm.cmd" | "pnpm.exe" | "npx" | "npx.cmd" | "npx.exe") 
        || exe.ends_with("/node") 
        || exe.ends_with("\\node.exe")
        || cmd.starts_with("node ")
        || cmd.contains(" node ")
        || cmd.ends_with(" node")
        || cmd.contains("/node ")
        || cmd.contains("\\node.exe") 
        || name.starts_with("node") {
        return Some((90, "node".into()));
    }
    // Bun
    if matches!(name, "bun" | "bun.exe" | "bunx" | "bunx.exe") 
        || exe.ends_with("/bun") 
        || exe.ends_with("\\bun.exe")
        || cmd.starts_with("bun ")
        || cmd.contains(" bun ")
        || cmd.ends_with(" bun")
        || cmd.contains("/bun ")
        || cmd.contains("\\bun.exe") 
        || name.starts_with("bun") {
        return Some((90, "bun".into()));
    }
    // Deno
    if matches!(name, "deno" | "deno.exe") || exe.ends_with("/deno") || exe.ends_with("\\deno.exe") 
        || cmd.starts_with("deno ") || cmd.contains(" deno ") || cmd.ends_with(" deno") || cmd.contains("/deno ") || cmd.contains("\\deno.exe") {
        return Some((85, "deno".into()));
    }
    // Python
    if name == "python" || name == "python3" || name == "python.exe" || name == "python3.exe"
        || exe.ends_with("/python") || exe.ends_with("/python3")
        || exe.ends_with("\\python.exe") || exe.ends_with("\\python3.exe")
    {
        return Some((80, "python".into()));
    }
    // Ruby
    if matches!(name, "ruby" | "ruby.exe") || exe.ends_with("/ruby") || exe.ends_with("\\ruby.exe") {
        return Some((75, "ruby".into()));
    }
    // Go
    if (name == "go" || name == "go.exe") && (exe.ends_with("/go") || exe.ends_with("\\go.exe")) {
        return Some((70, "go".into()));
    }
    // Java
    if matches!(name, "java" | "java.exe") || exe.ends_with("/java") || exe.ends_with("\\java.exe") {
        return Some((70, "java".into()));
    }
    // Cargo (Rust build)
    if matches!(name, "cargo" | "cargo.exe") || exe.ends_with("/cargo") || exe.ends_with("\\cargo.exe") {
        return Some((65, "cargo".into()));
    }
    // Cursor editor
    if name == "cursor" || name == "cursor.exe" || exe.contains("cursor") {
        return Some((60, "cursor".into()));
    }
    // VS Code
    if name == "code" || name == "code.exe" || exe.contains("visual studio code") || exe.contains("vscode") {
        return Some((60, "code".into()));
    }
    None
}

fn is_claude(name: &str, exe: &str, cmd: &str) -> bool {
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
