import { invoke } from "@tauri-apps/api/core";
import { listen, UnlistenFn } from "@tauri-apps/api/event";
import type { TerminalSession } from "../hooks/useSessionStore";

export async function createTerminal(
  sessionId: string,
  workingDir: string,
  cols: number,
  rows: number
): Promise<void> {
  return invoke("create_terminal", { sessionId, workingDir, cols, rows });
}

export async function writeInput(
  sessionId: string,
  data: string
): Promise<void> {
  return invoke("write_input", { sessionId, data });
}

export async function resizeTerminal(
  sessionId: string,
  cols: number,
  rows: number
): Promise<void> {
  return invoke("resize_terminal", { sessionId, cols, rows });
}

export async function getBufferedOutput(sessionId: string): Promise<string> {
  return invoke("get_buffered_output", { sessionId });
}

export async function suppressLiveOutput(
  sessionId: string,
  suppress: boolean
): Promise<void> {
  return invoke("suppress_live_output", { sessionId, suppress });
}

export async function destroyTerminal(sessionId: string): Promise<void> {
  return invoke("destroy_terminal", { sessionId });
}

export async function hasTerminal(sessionId: string): Promise<boolean> {
  return invoke("has_terminal", { sessionId });
}

export interface TerminalOutputEvent {
  sessionId: string;
  data: string; // base64
}

export async function onTerminalOutput(
  callback: (event: TerminalOutputEvent) => void
): Promise<UnlistenFn> {
  return listen<TerminalOutputEvent>("terminal-output", (e) =>
    callback(e.payload)
  );
}

export async function onProcessExited(
  callback: (sessionId: string) => void
): Promise<UnlistenFn> {
  return listen<string>("process-exited", (e) => callback(e.payload));
}

export async function getHotkey(): Promise<string> {
  return invoke<string>("get_hotkey");
}

export async function setHotkey(accelerator: string): Promise<void> {
  await invoke("set_hotkey", { accelerator });
}

export async function getRecentFolders(): Promise<string[]> {
  return invoke("get_recent_folders");
}

export async function clearRecentFolders(): Promise<void> {
  return invoke("clear_recent_folders");
}

export async function openRecentFolder(path: string): Promise<TerminalSession> {
  return invoke("open_recent_folder", { path });
}

export async function getPathColors(): Promise<Record<string, string>> {
  return invoke("get_path_colors");
}

export async function setPathColor(
  path: string,
  color: string
): Promise<void> {
  return invoke("set_path_color", { path, color });
}
