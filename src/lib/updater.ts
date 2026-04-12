import { check, Update } from "@tauri-apps/plugin-updater";
import { invoke } from "@tauri-apps/api/core";

export async function checkForUpdate(): Promise<Update | null> {
  const result = await check();
  return result ?? null;
}

export async function downloadUpdate(update: Update): Promise<void> {
  await update.downloadAndInstall();
}

export async function restartApp(): Promise<void> {
  await invoke("restart_app");
}
