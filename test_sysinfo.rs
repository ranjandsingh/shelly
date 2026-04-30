use sysinfo::{Pid, System};

fn main() {
    let mut system = System::new_all();
    system.refresh_all();
    
    for (pid, process) in system.processes() {
        let name = process.name().to_string_lossy().to_ascii_lowercase();
        if name.contains("node") || name.contains("bun") || name.contains("npm") {
            println!("PID: {}", pid);
            println!("Name: {}", process.name().to_string_lossy());
            println!("Exe: {:?}", process.exe().map(|p| p.to_string_lossy()));
            println!("Cmd: {:?}", process.cmd());
            println!("---");
        }
    }
}
