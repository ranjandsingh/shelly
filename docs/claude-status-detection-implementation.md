# Claude Status Detection: Implementation Plan

## Goal

Make Shelly's Claude status detection reliable by using process-aware detection as the primary signal and terminal text heuristics as secondary hints.

Current pain points:
- Status relies mostly on fragile text matching (`esc to interrupt`, `thinking with`, etc.).
- `parse_raw_output` exists but is not wired into the PTY read path.
- Completion detection is tied to specific UI glyph/text patterns that can change upstream.
- Viewport-only polling can misclassify status during redraws or scroll state changes.

## Scope

In scope:
- Track shell process PID per terminal session.
- Detect active Claude descendant processes for each session.
- Gate Claude-specific statuses based on real process state.
- Wire raw PTY parsing into backend read loop.
- Add confidence/stability logic to reduce status flapping.

Out of scope:
- Replacing interactive `claude --continue` with non-interactive `claude -p`.
- Building a separate daemon/service for process telemetry.
- UI redesign beyond exposing stronger status signals.

## Existing Architecture (Relevant)

- PTY creation and read loop: `src-tauri/src/pty.rs`
- Status classification: `src-tauri/src/status_parser.rs`
- Session state model: `src-tauri/src/session_store.rs`
- Frontend polling of visible text: `src/hooks/useTerminal.ts`

Observation:
- `StatusParser::parse_raw_output(...)` is currently unused in PTY read loop.
- Frontend invokes `parse_visible_text` every 500ms with visible viewport content.

## Proposed Design

### 1) Process Tracking Per Session

Add shell PID tracking in backend PTY state.

Changes:
- In `PtyInstance` (`pty.rs`), store:
  - `shell_pid: Option<u32>`
- In `create(...)`, keep the child handle long enough to capture `process_id()` and store it.
- Expose backend accessor:
  - `get_shell_pid(session_id: Uuid) -> Option<u32>`

Why:
- Gives a stable parent PID anchor for process-tree detection of Claude child processes.

### 2) Claude Runtime Detector

Create `src-tauri/src/claude_detector.rs`:
- Input: `shell_pid`
- Output:
  - `running: bool`
  - `claude_pid: Option<u32>`
  - `matched_cmd: Option<String>` (for debug logs only)

Detection strategy:
- Build process snapshot (cross-platform via `sysinfo` crate).
- Walk descendants of `shell_pid`.
- Match executable/name/cmdline for:
  - `claude`
  - `claude.exe`
  - optional fallback patterns in cmdline where binary path is wrapped
- Return strongest match.

Notes:
- Refresh process snapshot at bounded interval (e.g. 400-800ms) rather than every parser call.
- Keep detector pure/read-only and cheap.

### 3) Session Model Extensions

Extend `TerminalSession` (`session_store.rs`) with:
- `claude_running: bool` (default false)
- `claude_pid: Option<u32>` (default None)

Add store update method:
- `update_claude_runtime(session_id, running, pid)`

Emit updates through existing `sessions-updated` event pipeline so notch and frontend can consume with no extra transport layer.

### 4) Status Parser Changes (Primary = Process, Secondary = Text)

In `status_parser.rs`:

1. Add detector dependency and runtime check before classifying Claude states.
2. Decision gate:
   - If `claude_running == false`:
     - suppress Claude-specific transitions (`Working`, `WaitingForInput`, `TaskCompleted`) unless explicit non-Claude signals justify them.
     - default to `Idle`/`Interrupted` only when truly detected.
   - If `claude_running == true`:
     - allow current text heuristics and completion detection.
3. Confidence smoothing:
   - Require consecutive confirmations (e.g. 2 polls) before `WaitingForInput` and `TaskCompleted`.
   - Keep sticky `Working` timeout longer (target 3-5s).

### 5) Wire Raw Output Parsing

In `pty.rs` read loop (`read_loop`):
- On each `Ok(n)` chunk, call `status_parser.parse_raw_output(...)` before/after emitting `terminal-output` event.

Implementation options:
- Preferred: pass a lightweight callback/channel from `lib.rs`/AppState into PTY manager.
- Alternative: emit an internal event and handle parsing in command layer.

Requirement:
- Avoid blocking PTY reader thread; status parsing must remain non-blocking and panic-safe.

### 6) Frontend Polling Role

Keep visible-text polling (`useTerminal.ts`) but narrow responsibility:
- Prompt/menu shape detection (`WaitingForInput` hints).
- Secondary confirmation only when `claude_running` is true.

Do not rely on viewport text as source of truth for "Claude is currently running".

## Implementation Phases

### Phase 1: Runtime Foundation
- Add `sysinfo` dependency.
- Add shell PID capture in PTY.
- Implement `claude_detector.rs`.
- Add session runtime fields and update methods.

### Phase 2: Parser Integration
- Inject runtime detector into status parser path.
- Gate status transitions by process signal.
- Add confidence counters and sticky timing updates.

### Phase 3: Raw Path + Event Wiring
- Wire `parse_raw_output` into PTY read loop path.
- Ensure no thread safety regressions and no deadlocks.
- Emit updated session payloads.

### Phase 4: Hardening
- Add debug logs around runtime match/miss.
- Validate behavior across:
  - shell startup
  - `claude --continue` active task
  - prompt waiting states
  - interrupted runs
  - non-Claude commands in same shell

## Acceptance Criteria

- Shelly reports `Working` only when Claude is actually running (or within short sticky window).
- `WaitingForInput` no longer appears from unrelated terminal text.
- `TaskCompleted` detection remains responsive but avoids false positives.
- Status does not flap rapidly during redraw/resize/animation.
- Existing attention and sound flows still trigger correctly for true Claude states.

## Verification Plan

Manual scenarios:
1. Launch new session with `CLAUDE.md` and auto-launch enabled.
2. Start Claude task and verify:
   - runtime marked running
   - status enters `Working`
3. Trigger prompt requiring input and verify `WaitingForInput`.
4. Complete task and verify `TaskCompleted` then idle reset on interaction.
5. Run non-Claude shell commands; confirm Claude statuses do not trigger.
6. Interrupt Claude (`Esc`/Ctrl+C) and verify `Interrupted`.

Regression checks:
- Multiple sessions active with one background Claude run.
- Session switch while task is running.
- Panel animation/resize during status polling.

## Risks and Mitigations

- Risk: process matching false positives (other binaries named similarly).
  - Mitigation: strict executable/cmdline match rules + descendant-of-shell requirement.
- Risk: process snapshot overhead.
  - Mitigation: throttle refresh interval and cache recent results.
- Risk: PTY thread contention.
  - Mitigation: keep status work lightweight and off critical read path where needed.

## References

- Anthropic CLI reference: https://docs.anthropic.com/en/docs/claude-code/cli-usage
- Anthropic programmatic/stream output: https://docs.anthropic.com/en/docs/claude-code/headless
- portable-pty `Child::process_id`: https://docs.rs/portable-pty/latest/portable_pty/trait.Child.html
- sysinfo crate docs: https://docs.rs/sysinfo/latest/sysinfo/
