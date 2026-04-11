use std::sync::{Mutex, MutexGuard};

/// Lock a mutex, recovering gracefully from poison.
/// If a prior thread panicked while holding this lock, we log a warning
/// and recover the inner data rather than propagating the panic.
pub fn safe_lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    match mutex.lock() {
        Ok(guard) => guard,
        Err(poisoned) => {
            log::warn!("Recovering from poisoned mutex");
            poisoned.into_inner()
        }
    }
}
