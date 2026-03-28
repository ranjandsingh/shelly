# Notchy Windows — Plan & TODO

## TODO

### Phase 4: IDE Detection Wiring
- [x] Wire `IdeDetector` into `SessionStore` to auto-create sessions for detected projects
- [x] Poll every 5 seconds when panel is visible
- [x] Show green/gray dot for open/closed IDE projects
- [ ] Add "Add Folder" button for manual project folders (folder picker dialog)
- [ ] Resolve JetBrains project paths from recent projects XML

### Phase 5: Terminal Status Detection Wiring
- [x] Wire `StatusParser` to update tab status dots in real-time
- [ ] Read xterm.js visible buffer via JS interop for full-text classification
- [ ] Play sounds on status transitions (taskCompleted.mp3, waitingForInput.mp3)
- [ ] Add sound files to Resources/Sounds/

### Phase 6: Auto-Launch Claude
- [x] On session start, send `cd <project-dir> && cls && claude` if CLAUDE.md exists
- [x] Otherwise send `cd <project-dir> && cls`
- [ ] Detect shell type (PowerShell vs CMD) and adjust commands accordingly

### Phase 7: Git Checkpoints UI
- [x] Wire Ctrl+S shortcut in FloatingPanel to trigger `CheckpointManager.CreateCheckpoint`
- [x] Add checkpoint status bar at bottom of panel (shows "Checkpoint Saved [date]")
- [ ] Add restore button with confirmation dialog
- [ ] Add checkpoint history list (context menu or dropdown)

### Phase 8: Global Hotkey & Sleep Prevention Wiring
- [x] Wire `SleepPrevention.PreventSleep()` when any session status is `.Working`
- [x] Wire `SleepPrevention.AllowSleep()` when all sessions leave `.Working`
- [ ] Make hotkey configurable (settings file)

### Phase 9: Polish & UX
- [x] Panel auto-hide on deactivate when not pinned
- [x] Pin mode toggle (panel stays visible on deactivate)
- [x] Drag & drop folders onto panel to create sessions
- [x] Session persistence across app restarts (save/load JSON to AppData)
- [x] Tray icon context menu: list active sessions, switch on click
- [x] Keyboard shortcuts: Ctrl+T (new tab), Ctrl+W (close tab)
- [x] Active tab highlighting in SessionTabBar
- [x] Resize terminal (send cols/rows to ConPTY on panel resize)
- [x] Handle WebView2 resize messages to call `ConPtyTerminal.Resize()`
- [x] Single-instance app check (prevent multiple launches)
- [x] Wire core session switching (tab selection → terminal display + output handler)
- [ ] Proper error handling if WebView2 runtime is not installed
- [ ] App icon for exe/taskbar

### Future
- [ ] Settings UI (hotkey customization, shell preference, theme)
- [ ] Notification balloon on task completion
- [ ] Multi-monitor support (position panel on active monitor)
- [ ] Auto-update mechanism
