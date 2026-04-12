import type { Theme } from "../themes";

type VSCodeTheme = {
  name?: string;
  type?: "dark" | "light";
  colors?: Record<string, string>;
  tokenColors?: Array<{
    scope?: string | string[];
    settings?: { foreground?: string; background?: string; fontStyle?: string };
  }>;
};

/** Strip a trailing alpha byte from "#rrggbbaa" → "#rrggbb". */
export function stripAlpha(color: string | undefined): string | undefined {
  if (!color) return undefined;
  if (color.length === 9 && color.startsWith("#")) return color.slice(0, 7);
  if (color.length === 5 && color.startsWith("#")) return color.slice(0, 4);
  return color;
}

function hexToHsl(hex: string): { h: number; s: number; l: number } | null {
  const clean = stripAlpha(hex);
  if (!clean || !clean.startsWith("#") || clean.length !== 7) return null;
  const r = parseInt(clean.slice(1, 3), 16) / 255;
  const g = parseInt(clean.slice(3, 5), 16) / 255;
  const b = parseInt(clean.slice(5, 7), 16) / 255;
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const l = (max + min) / 2;
  let h = 0, s = 0;
  if (max !== min) {
    const d = max - min;
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    switch (max) {
      case r: h = (g - b) / d + (g < b ? 6 : 0); break;
      case g: h = (b - r) / d + 2; break;
      case b: h = (r - g) / d + 4; break;
    }
    h *= 60;
  }
  return { h, s, l };
}

function hslToHex(h: number, s: number, l: number): string {
  const c = (1 - Math.abs(2 * l - 1)) * s;
  const hp = h / 60;
  const x = c * (1 - Math.abs((hp % 2) - 1));
  let r = 0, g = 0, b = 0;
  if (hp >= 0 && hp < 1) { r = c; g = x; }
  else if (hp < 2) { r = x; g = c; }
  else if (hp < 3) { g = c; b = x; }
  else if (hp < 4) { g = x; b = c; }
  else if (hp < 5) { r = x; b = c; }
  else { r = c; b = x; }
  const m = l - c / 2;
  const toHex = (n: number) => Math.round((n + m) * 255).toString(16).padStart(2, "0");
  return `#${toHex(r)}${toHex(g)}${toHex(b)}`;
}

/** Shift HSL lightness by `delta` (−1..1). */
export function adjustL(color: string, delta: number): string {
  const hsl = hexToHsl(color);
  if (!hsl) return color;
  const l = Math.max(0, Math.min(1, hsl.l + delta));
  return hslToHex(hsl.h, hsl.s, l);
}

/** Strip line comments and block comments from JSONC, then parse. */
function parseJsonc(text: string): any {
  try { return JSON.parse(text); } catch {}
  const stripped = text
    .replace(/\/\/[^\n\r]*/g, "")
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/,\s*([}\]])/g, "$1");
  return JSON.parse(stripped);
}

const SYNTAX_MAP: Record<string, string[]> = {
  red: ["invalid", "keyword.control", "entity.name.tag"],
  green: ["string", "constant.character.escape"],
  yellow: ["constant.numeric", "entity.name.function"],
  blue: ["entity.name.function", "variable", "support.function"],
  magenta: ["keyword", "storage.type", "constant.language"],
  cyan: ["entity.name.type", "support.type", "support.class"],
};

function matchScope(rule: { scope?: string | string[] }, needle: string): boolean {
  const scope = rule.scope;
  if (!scope) return false;
  const scopes = Array.isArray(scope) ? scope : scope.split(",").map((s) => s.trim());
  return scopes.some((s) => s === needle || s.startsWith(needle + "."));
}

function findTokenColor(
  tokenColors: VSCodeTheme["tokenColors"] | undefined,
  scopes: string[]
): string | undefined {
  if (!tokenColors) return undefined;
  for (const s of scopes) {
    for (const rule of tokenColors) {
      if (matchScope(rule, s) && rule.settings?.foreground) {
        return stripAlpha(rule.settings.foreground);
      }
    }
  }
  return undefined;
}

function deriveAnsi(
  tokenColors: VSCodeTheme["tokenColors"] | undefined,
  fallback: string
): Pick<Theme["terminal"], "red" | "green" | "yellow" | "blue" | "magenta" | "cyan"> {
  const pick = (scopes: string[]): string =>
    findTokenColor(tokenColors, scopes) ?? fallback;
  return {
    red: pick(SYNTAX_MAP.red),
    green: pick(SYNTAX_MAP.green),
    yellow: pick(SYNTAX_MAP.yellow),
    blue: pick(SYNTAX_MAP.blue),
    magenta: pick(SYNTAX_MAP.magenta),
    cyan: pick(SYNTAX_MAP.cyan),
  };
}

function pick(colors: Record<string, string> | undefined, ...keys: string[]): string | undefined {
  if (!colors) return undefined;
  for (const k of keys) {
    const v = stripAlpha(colors[k]);
    if (v) return v;
  }
  return undefined;
}

/** Build a Theme from VS Code theme JSON (text or parsed object). */
export function parseVSCodeTheme(input: string | VSCodeTheme, displayName?: string): Theme {
  const raw: VSCodeTheme = typeof input === "string" ? parseJsonc(input) : input;
  if (typeof raw !== "object" || raw === null) {
    throw new Error("Theme JSON is not an object");
  }
  const colors = raw.colors || {};
  const isDark = raw.type !== "light";
  const fallbackBg = isDark ? "#1e1e1e" : "#ffffff";
  const fallbackFg = isDark ? "#cccccc" : "#333333";

  const background = pick(colors, "terminal.background", "editor.background") ?? fallbackBg;
  const foreground = pick(colors, "terminal.foreground", "editor.foreground") ?? fallbackFg;
  const cursor = pick(colors, "terminalCursor.foreground", "editorCursor.foreground") ?? foreground;
  const selectionBackground =
    pick(colors, "terminal.selectionBackground", "editor.selectionBackground") ??
    (isDark ? "#3a3a3a" : "#add6ff");

  const ansiFromColors = {
    black: pick(colors, "terminal.ansiBlack"),
    red: pick(colors, "terminal.ansiRed"),
    green: pick(colors, "terminal.ansiGreen"),
    yellow: pick(colors, "terminal.ansiYellow"),
    blue: pick(colors, "terminal.ansiBlue"),
    magenta: pick(colors, "terminal.ansiMagenta"),
    cyan: pick(colors, "terminal.ansiCyan"),
    white: pick(colors, "terminal.ansiWhite"),
  };
  const derived = deriveAnsi(raw.tokenColors, foreground);

  const red = ansiFromColors.red ?? derived.red;
  const green = ansiFromColors.green ?? derived.green;
  const yellow = ansiFromColors.yellow ?? derived.yellow;
  const blue = ansiFromColors.blue ?? derived.blue;
  const magenta = ansiFromColors.magenta ?? derived.magenta;
  const cyan = ansiFromColors.cyan ?? derived.cyan;
  const black = ansiFromColors.black ?? background;
  const white = ansiFromColors.white ?? foreground;

  const lift = (c: string) => adjustL(c, 0.08);

  const name = displayName ?? raw.name ?? "Imported theme";

  return {
    name,
    terminal: {
      background,
      foreground,
      cursor,
      selectionBackground,
      black,
      red,
      green,
      yellow,
      blue,
      magenta,
      cyan,
      white,
      brightBlack: pick(colors, "terminal.ansiBrightBlack") ?? lift(black),
      brightRed: pick(colors, "terminal.ansiBrightRed") ?? lift(red),
      brightGreen: pick(colors, "terminal.ansiBrightGreen") ?? lift(green),
      brightYellow: pick(colors, "terminal.ansiBrightYellow") ?? lift(yellow),
      brightBlue: pick(colors, "terminal.ansiBrightBlue") ?? lift(blue),
      brightMagenta: pick(colors, "terminal.ansiBrightMagenta") ?? lift(magenta),
      brightCyan: pick(colors, "terminal.ansiBrightCyan") ?? lift(cyan),
      brightWhite: pick(colors, "terminal.ansiBrightWhite") ?? lift(white),
    },
    chrome: {
      panelBg: background,
      tabBarBg: pick(colors, "editorGroupHeader.tabsBackground", "tab.inactiveBackground") ?? adjustL(background, -0.02),
      tabActiveBg: pick(colors, "tab.activeBackground") ?? background,
      tabHoverBg: pick(colors, "tab.hoverBackground") ?? adjustL(background, 0.03),
      tabText: pick(colors, "tab.inactiveForeground") ?? adjustL(foreground, -0.2),
      tabHoverText: pick(colors, "tab.activeForeground") ?? foreground,
      tabActiveText: pick(colors, "tab.activeForeground") ?? foreground,
      tabActiveRing: (() => {
        const accent = pick(colors, "tab.activeBorder") ?? foreground;
        return `${accent}28`;
      })(),
      dragBarBg: pick(colors, "editorGroupHeader.tabsBackground", "tab.inactiveBackground") ?? adjustL(background, -0.02),
      dragHandle: pick(colors, "tab.activeBorder") ?? adjustL(foreground, -0.3),
      border: pick(colors, "contrastBorder", "editorGroup.border") ?? adjustL(background, 0.05),
      menuBg: pick(colors, "menu.background") ?? adjustL(background, -0.01),
      menuText: pick(colors, "menu.foreground") ?? foreground,
      menuHover: pick(colors, "menubar.selectionBackground") ?? adjustL(background, 0.04),
      hintText: `${foreground}66`,
    },
  };
}

/** Validate a parsed Theme has usable ANSI colors — throws on failure. */
export function validateTheme(theme: Theme): void {
  const ansi = theme.terminal;
  const slots = ["black", "red", "green", "yellow", "blue", "magenta", "cyan", "white",
    "brightBlack", "brightRed", "brightGreen", "brightYellow", "brightBlue",
    "brightMagenta", "brightCyan", "brightWhite"] as const;
  const missing = slots.filter((s) => !/^#[0-9a-f]{6}$/i.test(ansi[s] ?? ""));
  if (missing.length > 0) {
    throw new Error(`Theme is missing or has malformed ANSI slots: ${missing.join(", ")}`);
  }
}
