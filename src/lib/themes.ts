export interface Theme {
  name: string;
  terminal: {
    background: string;
    foreground: string;
    cursor: string;
    selectionBackground: string;
    black: string;
    red: string;
    green: string;
    yellow: string;
    blue: string;
    magenta: string;
    cyan: string;
    white: string;
    brightBlack: string;
    brightRed: string;
    brightGreen: string;
    brightYellow: string;
    brightBlue: string;
    brightMagenta: string;
    brightCyan: string;
    brightWhite: string;
  };
  chrome: {
    panelBg: string;
    tabBarBg: string;
    tabActiveBg: string;
    tabHoverBg: string;
    tabText: string;
    tabHoverText: string;
    tabActiveText: string;
    tabActiveRing: string;
    dragBarBg: string;
    dragHandle: string;
    border: string;
    menuBg: string;
    menuText: string;
    menuHover: string;
    hintText: string;
  };
}

export { BUILTIN_THEMES } from "./themes/bundled";

export function hexToRgba(color: string, alpha: number): string {
  if (!color.startsWith("#") || (color.length !== 7 && color.length !== 4)) {
    return color;
  }
  let r: number, g: number, b: number;
  if (color.length === 4) {
    r = parseInt(color[1] + color[1], 16);
    g = parseInt(color[2] + color[2], 16);
    b = parseInt(color[3] + color[3], 16);
  } else {
    r = parseInt(color.slice(1, 3), 16);
    g = parseInt(color.slice(3, 5), 16);
    b = parseInt(color.slice(5, 7), 16);
  }
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

export function applyThemeToCSS(theme: Theme, opacity: number = 1, fadeContent: boolean = false) {
  const root = document.documentElement;
  const c = theme.chrome;
  const translucent = opacity < 1;
  const panelBg = translucent ? hexToRgba(c.panelBg, opacity) : c.panelBg;
  const tabBarBg = translucent ? hexToRgba(c.tabBarBg, opacity) : c.tabBarBg;
  const dragBarBg = translucent ? hexToRgba(c.dragBarBg, opacity) : c.dragBarBg;
  root.style.setProperty("--panel-bg", panelBg);
  root.style.setProperty("--tab-bar-bg", tabBarBg);
  root.style.setProperty("--tab-active-bg", c.tabActiveBg);
  root.style.setProperty("--tab-hover-bg", c.tabHoverBg);
  root.style.setProperty("--tab-text", c.tabText);
  root.style.setProperty("--tab-hover-text", c.tabHoverText);
  root.style.setProperty("--tab-active-text", c.tabActiveText);
  root.style.setProperty("--tab-active-ring", c.tabActiveRing);
  root.style.setProperty("--drag-bar-bg", dragBarBg);
  root.style.setProperty("--drag-handle", c.dragHandle);
  root.style.setProperty("--border-color", c.border);
  root.style.setProperty("--menu-bg", c.menuBg);
  root.style.setProperty("--menu-text", c.menuText);
  root.style.setProperty("--menu-hover", c.menuHover);
  root.style.setProperty("--hint-text", c.hintText);
  root.style.setProperty("--terminal-bg", theme.terminal.background);
  root.style.setProperty("--content-opacity", fadeContent ? String(opacity) : "1");
}

export function getTerminalTheme(theme: Theme, opacity: number = 1) {
  return {
    background: opacity < 1 ? "rgba(0, 0, 0, 0)" : theme.terminal.background,
    foreground: theme.terminal.foreground,
    cursor: theme.terminal.cursor,
    selectionBackground: theme.terminal.selectionBackground,
    black: theme.terminal.black,
    red: theme.terminal.red,
    green: theme.terminal.green,
    yellow: theme.terminal.yellow,
    blue: theme.terminal.blue,
    magenta: theme.terminal.magenta,
    cyan: theme.terminal.cyan,
    white: theme.terminal.white,
    brightBlack: theme.terminal.brightBlack,
    brightRed: theme.terminal.brightRed,
    brightGreen: theme.terminal.brightGreen,
    brightYellow: theme.terminal.brightYellow,
    brightBlue: theme.terminal.brightBlue,
    brightMagenta: theme.terminal.brightMagenta,
    brightCyan: theme.terminal.brightCyan,
    brightWhite: theme.terminal.brightWhite,
  };
}
