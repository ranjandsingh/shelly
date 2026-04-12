import { useCallback, useEffect, useMemo, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import type { Theme } from "../lib/themes";
import { BUILTIN_THEMES } from "../lib/themes";

type ImportedTheme = { id: string; theme: Theme; importedAt: string };

function slugify(name: string): string {
  return name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
}

function shortHash(s: string): string {
  let h = 0;
  for (let i = 0; i < s.length; i++) h = ((h << 5) - h + s.charCodeAt(i)) | 0;
  return Math.abs(h).toString(36).slice(0, 6);
}

export function useThemes() {
  const [imported, setImported] = useState<ImportedTheme[]>([]);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    invoke<ImportedTheme[]>("get_imported_themes")
      .then(setImported)
      .catch(() => setImported([]))
      .finally(() => setLoaded(true));
  }, []);

  const all = useMemo<Record<string, Theme>>(() => {
    const out: Record<string, Theme> = { ...BUILTIN_THEMES };
    for (const t of imported) out[t.id] = t.theme;
    return out;
  }, [imported]);

  const saveImported = useCallback(async (theme: Theme, existingId?: string) => {
    const used = new Set([...Object.keys(BUILTIN_THEMES), ...imported.map((t) => t.id)]);
    const base = `imported-${slugify(theme.name)}`;
    let id = existingId ?? `${base}-${shortHash(JSON.stringify(theme))}`;
    let suffix = 2;
    while (used.has(id) && id !== existingId) {
      id = `${base}-${shortHash(JSON.stringify(theme) + suffix)}`;
      suffix += 1;
    }
    const payload: ImportedTheme = {
      id,
      theme,
      importedAt: new Date().toISOString(),
    };
    const next = await invoke<ImportedTheme[]>("save_imported_theme", { theme: payload });
    setImported(next);
    return id;
  }, [imported]);

  const deleteImported = useCallback(async (id: string) => {
    const next = await invoke<ImportedTheme[]>("delete_imported_theme", { id });
    setImported(next);
  }, []);

  return { all, imported, loaded, saveImported, deleteImported };
}
