use rodio::{Decoder, OutputStream, Sink};
use std::io::Cursor;

pub fn play_task_completed() {
    std::thread::spawn(|| {
        // Try to load the sound from the resources directory at runtime
        let sound_data = std::env::current_exe()
            .ok()
            .and_then(|p| p.parent().map(|p| p.to_path_buf()))
            .map(|d| d.join("resources").join("task-complete.wav"))
            .and_then(|p| std::fs::read(&p).ok());

        if let Some(data) = sound_data {
            if let Ok((_stream, stream_handle)) = OutputStream::try_default() {
                if let Ok(source) = Decoder::new(Cursor::new(data)) {
                    if let Ok(sink) = Sink::try_new(&stream_handle) {
                        sink.append(source);
                        sink.sleep_until_end();
                    }
                }
            }
        } else {
            log::debug!("No task-complete.wav found, skipping sound");
        }
    });
}
