use portable_pty::{native_pty_system, CommandBuilder, MasterPty, PtySize};
use std::collections::HashMap;
use std::io::{Read, Write};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tauri::{AppHandle, Emitter};
use uuid::Uuid;
use base64::Engine;
use base64::engine::general_purpose::STANDARD as BASE64;
use crate::util::safe_lock;

const MAX_BUFFER_SIZE: usize = 2 * 1024 * 1024; // 2MB

struct PtyInstance {
    master_writer: Box<dyn Write + Send>,
    master: Box<dyn MasterPty + Send>,
    output_buffer: Vec<u8>,
    suppress_live: bool,
    shutdown: Arc<AtomicBool>,
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

        let shutdown = Arc::new(AtomicBool::new(false));
        let instance = Arc::new(Mutex::new(PtyInstance {
            master_writer: writer,
            master: pair.master,
            output_buffer: Vec::new(),
            suppress_live: false,
            shutdown: shutdown.clone(),
        }));

        {
            let mut instances = safe_lock(&self.instances);
            instances.insert(session_id, instance.clone());
        }

        // Spawn reader thread
        let sid = session_id;
        let reader_shutdown = shutdown.clone();
        std::thread::spawn(move || {
            Self::read_loop(reader, sid, instance, app, reader_shutdown);
        });

        log::info!("PtyManager: created terminal for session {session_id}");
        Ok(())
    }

    fn read_loop(
        mut reader: Box<dyn Read + Send>,
        session_id: Uuid,
        instance: Arc<Mutex<PtyInstance>>,
        app: AppHandle,
        shutdown: Arc<AtomicBool>,
    ) {
        let mut buf = [0u8; 4096];
        let mut pending = Vec::new();

        loop {
            // Check shutdown flag before blocking read
            if shutdown.load(Ordering::Acquire) {
                log::info!("PtyManager: shutdown signal for session {session_id}");
                // Flush any remaining pending data
                if !pending.is_empty() {
                    let b64 = BASE64.encode(&pending);
                    let suppress = safe_lock(&instance).suppress_live;
                    if !suppress {
                        let _ = app.emit("terminal-output", serde_json::json!({
                            "sessionId": session_id.to_string(),
                            "data": b64
                        }));
                    }
                }
                break;
            }

            match reader.read(&mut buf) {
                Ok(0) => {
                    // Flush pending before exit
                    if !pending.is_empty() {
                        let b64 = BASE64.encode(&pending);
                        let suppress = safe_lock(&instance).suppress_live;
                        if !suppress {
                            let _ = app.emit("terminal-output", serde_json::json!({
                                "sessionId": session_id.to_string(),
                                "data": b64
                            }));
                        }
                    }
                    let _ = app.emit("process-exited", session_id.to_string());
                    break;
                }
                Ok(n) => {
                    let data = &buf[..n];
                    pending.extend_from_slice(data);

                    // Append to instance buffer (cap at MAX_BUFFER_SIZE)
                    {
                        let mut inst = safe_lock(&instance);
                        inst.output_buffer.extend_from_slice(data);
                        if inst.output_buffer.len() > MAX_BUFFER_SIZE {
                            let keep_from = inst.output_buffer.len() - MAX_BUFFER_SIZE / 2;
                            inst.output_buffer = inst.output_buffer[keep_from..].to_vec();
                        }
                    }

                    // Flush: always emit after each read to avoid starvation
                    // (reader.read() blocks, so we can't rely on a timer to flush later).
                    // The 4KB read buffer provides natural batching during heavy output.
                    let b64 = BASE64.encode(&pending);
                    let suppress = safe_lock(&instance).suppress_live;
                    if !suppress {
                        let _ = app.emit("terminal-output", serde_json::json!({
                            "sessionId": session_id.to_string(),
                            "data": b64
                        }));
                    }
                    pending.clear();
                }
                Err(e) => {
                    // On shutdown, read errors are expected (pipe closed)
                    if shutdown.load(Ordering::Acquire) {
                        log::info!("PtyManager: read error after shutdown for session {session_id}: {e}");
                    } else {
                        log::warn!("PtyManager: read error for session {session_id}: {e}");
                    }
                    // Flush pending before exit
                    if !pending.is_empty() {
                        let b64 = BASE64.encode(&pending);
                        let suppress = safe_lock(&instance).suppress_live;
                        if !suppress {
                            let _ = app.emit("terminal-output", serde_json::json!({
                                "sessionId": session_id.to_string(),
                                "data": b64
                            }));
                        }
                    }
                    let _ = app.emit("process-exited", session_id.to_string());
                    break;
                }
            }
        }
        log::info!("PtyManager: read loop ended for session {session_id}");
    }

    pub fn write_input(&self, session_id: Uuid, data: &[u8]) -> Result<(), String> {
        let instances = safe_lock(&self.instances);
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let mut inst = safe_lock(instance);
        inst.master_writer.write_all(data)
            .map_err(|e| format!("Write failed: {e}"))?;
        inst.master_writer.flush()
            .map_err(|e| format!("Flush failed: {e}"))?;
        Ok(())
    }

    pub fn resize(&self, session_id: Uuid, cols: u16, rows: u16) -> Result<(), String> {
        let instances = safe_lock(&self.instances);
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let inst = safe_lock(instance);
        inst.master.resize(PtySize { rows, cols, pixel_width: 0, pixel_height: 0 })
            .map_err(|e| format!("Resize failed: {e}"))?;
        Ok(())
    }

    pub fn get_buffered_output(&self, session_id: Uuid) -> Result<String, String> {
        let instances = safe_lock(&self.instances);
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let inst = safe_lock(instance);
        Ok(BASE64.encode(&inst.output_buffer))
    }

    pub fn suppress_live_output(&self, session_id: Uuid, suppress: bool) -> Result<(), String> {
        let instances = safe_lock(&self.instances);
        let instance = instances.get(&session_id)
            .ok_or("Session not found")?;
        let mut inst = safe_lock(instance);
        inst.suppress_live = suppress;
        Ok(())
    }

    pub fn destroy(&self, session_id: Uuid) {
        // Signal the reader thread to stop before dropping the PTY handle
        {
            let instances = safe_lock(&self.instances);
            if let Some(inst_arc) = instances.get(&session_id) {
                let inst = safe_lock(inst_arc);
                inst.shutdown.store(true, Ordering::Release);
            }
        }
        // Brief sleep to give the reader thread a chance to see the flag
        // and exit before we drop the master handle
        std::thread::sleep(Duration::from_millis(50));
        // Now remove (and drop) the instance
        let mut instances = safe_lock(&self.instances);
        instances.remove(&session_id);
        log::info!("PtyManager: destroyed terminal for session {session_id}");
    }

    pub fn has_terminal(&self, session_id: Uuid) -> bool {
        let instances = safe_lock(&self.instances);
        instances.contains_key(&session_id)
    }
}
