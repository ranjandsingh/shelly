import { useEffect, useMemo, useRef, useState } from "react";
import { open as openDialog } from "@tauri-apps/plugin-dialog";
import { readTextFile } from "@tauri-apps/plugin-fs";
import { BUILTIN_THEMES, applyThemeToCSS } from "../lib/themes";
import { parseVSCodeTheme, validateTheme } from "../lib/themes/parse";
import { useThemes } from "../hooks/useThemes";

interface ThemesModalProps {
  currentThemeId: string;
  currentOpacity: number;
  currentFadeContent: boolean;
  currentFontSize: number;
  onSaved: (themeId: string, opacity: number, fadeContent: boolean, fontSize: number) => void;
  onClose: () => void;
}

const DEFAULT_THEME_ID = "vs-dark";
const DEFAULT_FONT_SIZE = 11;
const FONT_SIZES = [
  { label: "Small", size: 9 },
  { label: "Medium", size: 11 },
  { label: "Large", size: 14 },
  { label: "Extra Large", size: 18 },
];

function Swatch({ theme }: { theme: { terminal: any; chrome: any } }) {
  return (
    <span
      className="themes-modal-swatch"
      style={{ background: theme.terminal.background, borderColor: theme.chrome.border }}
    >
      <span style={{ background: theme.terminal.yellow }} />
      <span style={{ background: theme.terminal.magenta }} />
      <span style={{ background: theme.terminal.foreground }} />
    </span>
  );
}

export function ThemesModal({
  currentThemeId,
  currentOpacity,
  currentFadeContent,
  currentFontSize,
  onSaved,
  onClose,
}: ThemesModalProps) {
  const { all, imported, saveImported, deleteImported } = useThemes();
  const originalRef = useRef({
    themeId: currentThemeId,
    opacity: currentOpacity,
    fadeContent: currentFadeContent,
    fontSize: currentFontSize,
  });

  const [focusedId, setFocusedId] = useState(currentThemeId);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [opacity, setOpacity] = useState(currentOpacity);
  const [fadeContent, setFadeContent] = useState(currentFadeContent);
  const [fontSize, setFontSize] = useState(currentFontSize);
  const [importError, setImportError] = useState<string | null>(null);
  const [pasteOpen, setPasteOpen] = useState(false);
  const [pasteText, setPasteText] = useState("");

  const importedIds = useMemo(() => new Set(imported.map((t) => t.id)), [imported]);

  const list = useMemo(() => {
    const q = search.trim().toLowerCase();
    const entries = Object.entries(all);
    const filtered = q
      ? entries.filter(([_, t]) => t.name.toLowerCase().includes(q))
      : entries;
    return filtered.map(([id, theme]) => ({ id, theme, isImported: importedIds.has(id) }));
  }, [all, search, importedIds]);

  // Preview whenever focused theme, opacity, or fade toggles change.
  useEffect(() => {
    const theme = all[focusedId];
    if (theme) applyThemeToCSS(theme, opacity, fadeContent);
  }, [all, focusedId, opacity, fadeContent]);

  const revert = () => {
    const theme = all[originalRef.current.themeId] ?? BUILTIN_THEMES[DEFAULT_THEME_ID];
    applyThemeToCSS(theme, originalRef.current.opacity, originalRef.current.fadeContent);
  };

  const handleCancel = () => {
    revert();
    onClose();
  };

  const handleSave = () => {
    onSaved(focusedId, opacity, fadeContent, fontSize);
    onClose();
  };

  const handleReset = () => {
    setFocusedId(DEFAULT_THEME_ID);
    setOpacity(1);
    setFadeContent(false);
    setFontSize(DEFAULT_FONT_SIZE);
    queueMicrotask(() => {
      onSaved(DEFAULT_THEME_ID, 1, false, DEFAULT_FONT_SIZE);
      onClose();
    });
  };

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        if (pickerOpen) { setPickerOpen(false); setSearch(""); return; }
        handleCancel();
        return;
      }
      if (e.key === "Enter") {
        e.preventDefault();
        if (pickerOpen) { setPickerOpen(false); return; }
        handleSave();
        return;
      }
      if (!pickerOpen) return;
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        const idx = list.findIndex((row) => row.id === focusedId);
        if (idx < 0) return;
        const next = e.key === "ArrowDown"
          ? Math.min(list.length - 1, idx + 1)
          : Math.max(0, idx - 1);
        setFocusedId(list[next].id);
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [list, focusedId, opacity, fadeContent, pickerOpen]);

  const handleImportFile = async () => {
    setImportError(null);
    try {
      const picked = await openDialog({
        multiple: false,
        filters: [{ name: "VS Code theme JSON", extensions: ["json"] }],
      });
      if (!picked) return;
      const path = Array.isArray(picked) ? picked[0] : picked;
      const text = await readTextFile(path);
      const theme = parseVSCodeTheme(text);
      validateTheme(theme);
      const id = await saveImported(theme);
      setFocusedId(id);
    } catch (e: any) {
      setImportError(`Couldn't import: ${e?.message ?? String(e)}`);
    }
  };

  const handlePasteImport = async () => {
    setImportError(null);
    try {
      const theme = parseVSCodeTheme(pasteText);
      validateTheme(theme);
      const id = await saveImported(theme);
      setFocusedId(id);
      setPasteOpen(false);
      setPasteText("");
    } catch (e: any) {
      setImportError(`Couldn't parse pasted JSON: ${e?.message ?? String(e)}`);
    }
  };

  const handleDelete = async (id: string) => {
    const idx = list.findIndex((row) => row.id === id);
    await deleteImported(id);
    if (focusedId === id) {
      const fallback = list[Math.max(0, idx - 1)]?.id ?? Object.keys(BUILTIN_THEMES)[0];
      setFocusedId(fallback);
      if (originalRef.current.themeId === id) {
        originalRef.current.themeId = fallback;
      }
    }
  };

  const focusedTheme = all[focusedId] ?? BUILTIN_THEMES[DEFAULT_THEME_ID];

  return (
    <div className="themes-modal-overlay" onClick={handleCancel}>
      <div className="themes-modal" onClick={(e) => e.stopPropagation()}>
        <div className="themes-modal-header">
          <span>Appearance</span>
          <button className="themes-modal-close" onClick={handleCancel}>×</button>
        </div>

        <div className="themes-picker">
          <button
            type="button"
            className={`themes-picker-trigger ${pickerOpen ? "open" : ""}`}
            onClick={() => setPickerOpen((v) => !v)}
          >
            <Swatch theme={focusedTheme} />
            <span className="themes-picker-name">{focusedTheme.name}</span>
            <span className="themes-picker-caret">▾</span>
          </button>

          {pickerOpen && (
            <div className="themes-picker-pop">
              <input
                className="themes-modal-search"
                type="text"
                placeholder="Search themes…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                autoFocus
              />
              <div className="themes-modal-list" role="listbox">
                {list.map((row) => (
                  <div
                    key={row.id}
                    role="option"
                    aria-selected={row.id === focusedId}
                    className={`themes-modal-row ${row.id === focusedId ? "focused" : ""}`}
                    onClick={() => { setFocusedId(row.id); setPickerOpen(false); setSearch(""); }}
                  >
                    <Swatch theme={row.theme} />
                    <span className="themes-modal-name">{row.theme.name}</span>
                    {row.id === currentThemeId && <span className="themes-modal-badge saved">saved</span>}
                    {row.isImported && <span className="themes-modal-badge imported">imported</span>}
                    {row.isImported && (
                      <button
                        className="themes-modal-delete"
                        onClick={(e) => { e.stopPropagation(); handleDelete(row.id); }}
                        title="Delete imported theme"
                      >×</button>
                    )}
                  </div>
                ))}
                {list.length === 0 && (
                  <div className="themes-modal-empty">No themes match "{search}"</div>
                )}
              </div>
            </div>
          )}
        </div>

        <div className="themes-modal-import">
          <button onClick={handleImportFile}>Import VS Code theme…</button>
          <button onClick={() => setPasteOpen((v) => !v)}>Paste JSON…</button>
        </div>
        {pasteOpen && (
          <div className="themes-modal-paste">
            <textarea
              value={pasteText}
              onChange={(e) => setPasteText(e.target.value)}
              placeholder="Paste VS Code theme JSON here"
              rows={4}
            />
            <button onClick={handlePasteImport}>Import</button>
          </div>
        )}
        {importError && <div className="themes-modal-error">{importError}</div>}

        <div className="themes-modal-section">Text</div>
        <div className="themes-modal-row-ctrl">
          <span>Text size</span>
          <div className="themes-size-buttons">
            {FONT_SIZES.map((s) => (
              <button
                key={s.size}
                type="button"
                className={`themes-size-btn ${fontSize === s.size ? "active" : ""}`}
                onClick={() => setFontSize(s.size)}
                title={`${s.size}px`}
              >
                {s.label}
              </button>
            ))}
          </div>
        </div>

        <div className="themes-modal-section">Transparency</div>
        <div className="themes-modal-row-ctrl">
          <span>Panel transparency</span>
          <input
            type="range"
            min={0}
            max={50}
            step={5}
            value={Math.round((1 - opacity) * 100)}
            onChange={(e) => setOpacity(1 - parseInt(e.target.value, 10) / 100)}
          />
          <span className="themes-modal-value">{Math.round((1 - opacity) * 100)}%</span>
        </div>
        <div className="themes-modal-row-ctrl">
          <span>Translucent content</span>
          <button
            className={`themes-modal-toggle ${fadeContent ? "on" : ""}`}
            onClick={() => setFadeContent((v) => !v)}
          >
            {fadeContent ? "ON" : "OFF"}
          </button>
        </div>

        <div className="themes-modal-buttons">
          <button className="themes-modal-reset" onClick={handleReset}>Reset</button>
          <div className="themes-modal-buttons-right">
            <button className="themes-modal-cancel" onClick={handleCancel}>Cancel</button>
            <button className="themes-modal-save" onClick={handleSave}>Save</button>
          </div>
        </div>
      </div>
    </div>
  );
}
