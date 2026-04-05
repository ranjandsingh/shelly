use std::env;
use std::path::{Path, PathBuf};

#[derive(Clone, Debug, serde::Serialize)]
pub struct ShellInfo {
    pub label: String,
    pub path: String,
}

pub fn detect_default_shell() -> String {
    #[cfg(target_os = "windows")]
    {
        if let Some(git_bash) = find_git_bash() {
            return git_bash;
        }
        env::var("COMSPEC").unwrap_or_else(|_| r"C:\Windows\System32\cmd.exe".into())
    }
    #[cfg(not(target_os = "windows"))]
    {
        env::var("SHELL").unwrap_or_else(|_| "/bin/zsh".into())
    }
}

pub fn get_available_shells() -> Vec<ShellInfo> {
    let mut shells = Vec::new();

    #[cfg(target_os = "windows")]
    {
        if let Some(path) = find_git_bash() {
            shells.push(ShellInfo { label: "Git Bash".into(), path });
        }
        let system32 = env::var("SystemRoot")
            .map(|r| format!(r"{r}\System32"))
            .unwrap_or_else(|_| r"C:\Windows\System32".into());
        let wsl_bash = format!(r"{system32}\bash.exe");
        if Path::new(&wsl_bash).exists() {
            shells.push(ShellInfo { label: "WSL".into(), path: wsl_bash });
        }
        let cmd = env::var("COMSPEC").unwrap_or_else(|_| r"C:\Windows\System32\cmd.exe".into());
        if Path::new(&cmd).exists() {
            shells.push(ShellInfo { label: "Command Prompt (cmd)".into(), path: cmd });
        }
        if let Some(pwsh) = which_on_path("pwsh.exe") {
            shells.push(ShellInfo { label: "PowerShell 7 (pwsh)".into(), path: pwsh });
        }
        let win_ps = format!(r"{system32}\WindowsPowerShell\v1.0\powershell.exe");
        if Path::new(&win_ps).exists() {
            shells.push(ShellInfo { label: "Windows PowerShell".into(), path: win_ps });
        }
    }

    #[cfg(target_os = "macos")]
    {
        if Path::new("/bin/zsh").exists() {
            shells.push(ShellInfo { label: "zsh".into(), path: "/bin/zsh".into() });
        }
        if Path::new("/bin/bash").exists() {
            shells.push(ShellInfo { label: "bash".into(), path: "/bin/bash".into() });
        }
        if Path::new("/opt/homebrew/bin/bash").exists() {
            shells.push(ShellInfo { label: "bash (Homebrew)".into(), path: "/opt/homebrew/bin/bash".into() });
        }
        for fish_path in &["/opt/homebrew/bin/fish", "/usr/local/bin/fish"] {
            if Path::new(fish_path).exists() {
                shells.push(ShellInfo { label: "fish".into(), path: fish_path.to_string() });
                break;
            }
        }
    }

    shells
}

#[cfg(target_os = "windows")]
fn find_git_bash() -> Option<String> {
    let candidates = [
        r"C:\Program Files\Git\bin\bash.exe",
        r"C:\Program Files (x86)\Git\bin\bash.exe",
    ];
    for c in &candidates {
        if Path::new(c).exists() {
            return Some(c.to_string());
        }
    }
    if let Ok(local) = env::var("LOCALAPPDATA") {
        let p = format!(r"{local}\Programs\Git\bin\bash.exe");
        if Path::new(&p).exists() {
            return Some(p);
        }
    }
    // PATH lookup excluding System32 (that's WSL bash)
    which_on_path_excluding("bash.exe", "System32")
}

fn which_on_path(exe: &str) -> Option<String> {
    let path_var = env::var("PATH").ok()?;
    let sep = if cfg!(windows) { ';' } else { ':' };
    for dir in path_var.split(sep) {
        let full = PathBuf::from(dir.trim()).join(exe);
        if full.exists() {
            return Some(full.to_string_lossy().into());
        }
    }
    None
}

#[cfg(target_os = "windows")]
fn which_on_path_excluding(exe: &str, exclude_containing: &str) -> Option<String> {
    let path_var = env::var("PATH").ok()?;
    for dir in path_var.split(';') {
        let trimmed = dir.trim();
        if trimmed.to_lowercase().contains(&exclude_containing.to_lowercase()) {
            continue;
        }
        let full = PathBuf::from(trimmed).join(exe);
        if full.exists() {
            return Some(full.to_string_lossy().into());
        }
    }
    None
}
