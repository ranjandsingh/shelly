type FocusFn = () => void;

let handler: FocusFn | null = null;

/** Called by useTerminal once the xterm instance is ready. */
export function registerTerminalFocus(fn: FocusFn | null): void {
  handler = fn;
}

/** Called by useAttention to force-focus the active terminal. */
export function focusActiveTerminal(): void {
  handler?.();
}
