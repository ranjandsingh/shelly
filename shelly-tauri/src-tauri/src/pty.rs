use portable_pty::{native_pty_system, CommandBuilder, MasterPty, PtySize};
use std::collections::HashMap;
use std::io::{Read, Write};
use std::sync::{Arc, Mutex};
use tauri::{AppHandle, Emitter};
use uuid::Uuid;
use base64::Engine;
use base64::engine::general_purpose::STANDARD as BASE64;

const MAX_BUFFER_SIZE: usize = 2 * 1024 * 1024; // 2MB

struct PtyInstance {
    master_writer: Box<dyn Write + Send>,
    master: Box<dyn MasterPty + Send>,
    output_buffer: Vec<u8>,
    suppress_live: bool,
}

pub struct PtyManager {
    instances: Mutex<HashMap<Uuid, Arc<Mutex<PtyInstance>>>>,
}

impl PtyManager {
    pub fn new() -> Self {
        Self {
            instances: Mutex::new(HashMap::new()),
        }
    }

    pub fn create(
        &self,
        session_id: Uuid,
        working_dir: &str,
        shell_path: &str,
        cols: u16,
        rows: u16,
        app: AppHandle,
    ) -> Result<(), String> {
        let pty_system = native_pty_system();
        let pair = pty_system
            .openpty(PtySize {
                rows,
                cols,
                pixel_width: 0,
                pixel_height: 0,
            })
            .map_err(|e| format!("Failed to open PTY: {e}"))?;

        let mut cmd = CommandBuilder::new(shell_path);
        cmd.cwd(working_dir);

        let _child = pair.slave.spawn_command(cmd)
            .map_err(|e| format!("Failed to spawn shell: {e}"))?;

        drop(pair.slave);

        let reader = pair.master.try_clone_reader()
            .map_err(|e| format!("Failed to clone reader: {e}"))?;
        let writer = pair.master.take_writer()
            .map_err(|e| format!("Failed to take writer: {e}"))?;

        let instance = Arc::new(Mutex::new(PtyInstance {
            master_writer: writer,
            master: pair.master,
            output_buffer: Vec::new(),
            suppress_live: false,
        }));

        {
            let mut instances = self.instances.lock().unwrap();
            instances.insert(session_id, instance.clone());
        }

        // Spawn reader thread
        let sid = session_id;
        std::thread::spawn(move || {
            Self::read_loop(reader, sid, instance, app);
        });

        log::info!("PtyManager: created terminal for session {session_id}");
        Ok(())
    }

    fn read_loop(
        mut reader: Box<dyn Read + Send>,
        session_id: Uuid,
        instance: Arc<Mutex<PtyInstance>>,
        app: AppHandle,
    ) {
        let mut buf = [0u8; 4096];
        loop {
            match reader.read(&mut buf) {
                Ok(0) => {
                    let _ = app.emit("process-exited", session_id.to_string());
                    break;
                }
                Ok(n) => {
                    let data = buf[..n].to_vec();
                    let b64 = BASE64.encode(&data);

                    let mut inst = instance.lock().unwrap();
                    // Append to buffer (cap at MAX_BUFFER_SIZE)
                    inst.output_buffer.extend_from_slice(&data);
                    if inst.output_buffer.len() > MAX_BUFFER_SIZE {
                        let keep_from = inst.output_buffer.len() - MAX_BUFFER_SIZE / 2;
                        inst.output_buffer = inst.output_buffer[keep_from..].to_vec();
                    }

                    let suppress = inst.suppress_live;
                    drop(inst); // release lock before emit

                    if !suppress {
                        let _ = app.emit("terminal-output", serde_json::json!({
                            "sessionId": session_id.to_string(),
                            "data": b64
                        }));
                    }
                }
                Err(_) => {
                    let _ = app.emit("process-exited", session_id.to_string());
                    break;
                }
            }
        }
        log::info!("PtyManager: read loop ended for session {session_id}");
    }

    pub fn write_input(&self, session_id: Uuid, data: &[u8]) -> Result<(), String> {
        let instances = self.instances.lock().unwrap();
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let mut inst = instance.lock().unwrap();
        inst.master_writer.write_all(data)
            .map_err(|e| format!("Write failed: {e}"))?;
        inst.master_writer.flush()
            .map_err(|e| format!("Flush failed: {e}"))?;
        Ok(())
    }

    pub fn resize(&self, session_id: Uuid, cols: u16, rows: u16) -> Result<(), String> {
        let instances = self.instances.lock().unwrap();
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let inst = instance.lock().unwrap();
        inst.master.resize(PtySize { rows, cols, pixel_width: 0, pixel_height: 0 })
            .map_err(|e| format!("Resize failed: {e}"))?;
        Ok(())
    }

    pub fn get_buffered_output(&self, session_id: Uuid) -> Result<String, String> {
        let instances = self.instances.lock().unwrap();
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let inst = instance.lock().unwrap();
        Ok(BASE64.encode(&inst.output_buffer))
    }

    pub fn suppress_live_output(&self, session_id: Uuid, suppress: bool) -> Result<(), String> {
        let instances = self.instances.lock().unwrap();
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let mut inst = instance.lock().unwrap();
        inst.suppress_live = suppress;
        Ok(())
    }

    pub fn destroy(&self, session_id: Uuid) {
        let mut instances = self.instances.lock().unwrap();
        instances.remove(&session_id);
        log::info!("PtyManager: destroyed terminal for session {session_id}");
    }

    pub fn has_terminal(&self, session_id: Uuid) -> bool {
        let instances = self.instances.lock().unwrap();
        instances.contains_key(&session_id)
    }
}
