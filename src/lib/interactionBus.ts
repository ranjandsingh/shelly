import { invoke } from "@tauri-apps/api/core";

type Handler = (sessionId: string) => void;

const handlers = new Set<Handler>();
const lastEmitAt = new Map<string, number>();
const DEBOUNCE_MS = 250;

export function onSessionInteraction(fn: Handler): () => void {
  handlers.add(fn);
  return () => {
    handlers.delete(fn);
  };
}

/** Called by useTerminal on key/scroll/mousedown. */
export function emitSessionInteraction(sessionId: string): void {
  const now = Date.now();
  const prev = lastEmitAt.get(sessionId) ?? 0;
  if (now - prev < DEBOUNCE_MS) return;
  lastEmitAt.set(sessionId, now);

  for (const h of handlers) h(sessionId);
  invoke("mark_session_interacted", { sessionId }).catch((e) => {
    console.warn("[interactionBus] mark_session_interacted failed:", e);
  });
}
