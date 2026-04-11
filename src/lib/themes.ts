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

export const THEMES: Record<string, Theme> = {
  "vs-dark": {
    name: "Dark+ (Default)",
    terminal: {
      background: "#1e1e1e",
      foreground: "#d4d4d4",
      cursor: "#d4d4d4",
      selectionBackground: "#264f78",
      black: "#000000",
      red: "#cd3131",
      green: "#0dbc79",
      yellow: "#e5e510",
      blue: "#2472c8",
      magenta: "#bc3fbc",
      cyan: "#11a8cd",
      white: "#e5e5e5",
      brightBlack: "#666666",
      brightRed: "#f14c4c",
      brightGreen: "#23d18b",
      brightYellow: "#f5f543",
      brightBlue: "#3b8eea",
      brightMagenta: "#d670d6",
      brightCyan: "#29b8db",
      brightWhite: "#e5e5e5",
    },
    chrome: {
      panelBg: "#1e1e1e",
      tabBarBg: "#181818",
      tabActiveBg: "#1e1e1e",
      tabHoverBg: "#222",
      tabText: "#888",
      tabHoverText: "#bbb",
      tabActiveText: "#e6e6e6",
      tabActiveRing: "rgba(255,255,255,0.06)",
      dragBarBg: "#181818",
      dragHandle: "#444",
      border: "#333",
      menuBg: "#2a2a2a",
      menuText: "#ddd",
      menuHover: "#333",
      hintText: "rgba(255,255,255,0.40)",
    },
  },
  monokai: {
    name: "Monokai",
    terminal: {
      background: "#272822",
      foreground: "#f8f8f2",
      cursor: "#f8f8f2",
      selectionBackground: "#49483e",
      black: "#272822",
      red: "#f92672",
      green: "#a6e22e",
      yellow: "#f4bf75",
      blue: "#66d9ef",
      magenta: "#ae81ff",
      cyan: "#a1efe4",
      white: "#f8f8f2",
      brightBlack: "#75715e",
      brightRed: "#f92672",
      brightGreen: "#a6e22e",
      brightYellow: "#f4bf75",
      brightBlue: "#66d9ef",
      brightMagenta: "#ae81ff",
      brightCyan: "#a1efe4",
      brightWhite: "#f9f8f5",
    },
    chrome: {
      panelBg: "#272822",
      tabBarBg: "#21221c",
      tabActiveBg: "#272822",
      tabHoverBg: "#2d2e26",
      tabText: "#8a8670",
      tabHoverText: "#b5b09a",
      tabActiveText: "#f8f8f2",
      tabActiveRing: "rgba(248,248,242,0.06)",
      dragBarBg: "#21221c",
      dragHandle: "#75715e",
      border: "#3e3d32",
      menuBg: "#3e3d32",
      menuText: "#f8f8f2",
      menuHover: "#49483e",
      hintText: "rgba(248,248,242,0.35)",
    },
  },
  dracula: {
    name: "Dracula",
    terminal: {
      background: "#282a36",
      foreground: "#f8f8f2",
      cursor: "#f8f8f2",
      selectionBackground: "#44475a",
      black: "#21222c",
      red: "#ff5555",
      green: "#50fa7b",
      yellow: "#f1fa8c",
      blue: "#bd93f9",
      magenta: "#ff79c6",
      cyan: "#8be9fd",
      white: "#f8f8f2",
      brightBlack: "#6272a4",
      brightRed: "#ff6e6e",
      brightGreen: "#69ff94",
      brightYellow: "#ffffa5",
      brightBlue: "#d6acff",
      brightMagenta: "#ff92df",
      brightCyan: "#a4ffff",
      brightWhite: "#ffffff",
    },
    chrome: {
      panelBg: "#282a36",
      tabBarBg: "#21222e",
      tabActiveBg: "#282a36",
      tabHoverBg: "#2e3040",
      tabText: "#6878a8",
      tabHoverText: "#8e9cc5",
      tabActiveText: "#f8f8f2",
      tabActiveRing: "rgba(248,248,242,0.06)",
      dragBarBg: "#21222e",
      dragHandle: "#6272a4",
      border: "#44475a",
      menuBg: "#44475a",
      menuText: "#f8f8f2",
      menuHover: "#6272a4",
      hintText: "rgba(248,248,242,0.35)",
    },
  },
  "solarized-dark": {
    name: "Solarized Dark",
    terminal: {
      background: "#002b36",
      foreground: "#839496",
      cursor: "#839496",
      selectionBackground: "#073642",
      black: "#073642",
      red: "#dc322f",
      green: "#859900",
      yellow: "#b58900",
      blue: "#268bd2",
      magenta: "#d33682",
      cyan: "#2aa198",
      white: "#eee8d5",
      brightBlack: "#586e75",
      brightRed: "#cb4b16",
      brightGreen: "#586e75",
      brightYellow: "#657b83",
      brightBlue: "#839496",
      brightMagenta: "#6c71c4",
      brightCyan: "#93a1a1",
      brightWhite: "#fdf6e3",
    },
    chrome: {
      panelBg: "#002b36",
      tabBarBg: "#00222c",
      tabActiveBg: "#002b36",
      tabHoverBg: "#003340",
      tabText: "#5a7880",
      tabHoverText: "#7a9ea6",
      tabActiveText: "#839496",
      tabActiveRing: "rgba(131,148,150,0.08)",
      dragBarBg: "#00222c",
      dragHandle: "#586e75",
      border: "#073642",
      menuBg: "#073642",
      menuText: "#839496",
      menuHover: "#002b36",
      hintText: "rgba(131,148,150,0.40)",
    },
  },
  "one-dark": {
    name: "One Dark",
    terminal: {
      background: "#282c34",
      foreground: "#abb2bf",
      cursor: "#528bff",
      selectionBackground: "#3e4451",
      black: "#282c34",
      red: "#e06c75",
      green: "#98c379",
      yellow: "#e5c07b",
      blue: "#61afef",
      magenta: "#c678dd",
      cyan: "#56b6c2",
      white: "#abb2bf",
      brightBlack: "#5c6370",
      brightRed: "#e06c75",
      brightGreen: "#98c379",
      brightYellow: "#e5c07b",
      brightBlue: "#61afef",
      brightMagenta: "#c678dd",
      brightCyan: "#56b6c2",
      brightWhite: "#ffffff",
    },
    chrome: {
      panelBg: "#282c34",
      tabBarBg: "#21252b",
      tabActiveBg: "#282c34",
      tabHoverBg: "#2c3039",
      tabText: "#7a8290",
      tabHoverText: "#9aa2b0",
      tabActiveText: "#abb2bf",
      tabActiveRing: "rgba(171,178,191,0.06)",
      dragBarBg: "#21252b",
      dragHandle: "#5c6370",
      border: "#3e4451",
      menuBg: "#3e4451",
      menuText: "#abb2bf",
      menuHover: "#4b5263",
      hintText: "rgba(171,178,191,0.40)",
    },
  },
  "vs-light": {
    name: "Light+ (VS Code)",
    terminal: {
      background: "#ffffff",
      foreground: "#333333",
      cursor: "#333333",
      selectionBackground: "#add6ff",
      black: "#000000",
      red: "#cd3131",
      green: "#00bc7c",
      yellow: "#949800",
      blue: "#0451a5",
      magenta: "#bc05bc",
      cyan: "#0598bc",
      white: "#555555",
      brightBlack: "#666666",
      brightRed: "#cd3131",
      brightGreen: "#14ce14",
      brightYellow: "#b5ba00",
      brightBlue: "#0451a5",
      brightMagenta: "#bc05bc",
      brightCyan: "#0598bc",
      brightWhite: "#a5a5a5",
    },
    chrome: {
      panelBg: "#ffffff",
      tabBarBg: "#e8e8e8",
      tabActiveBg: "#ffffff",
      tabHoverBg: "#efefef",
      tabText: "#777",
      tabHoverText: "#444",
      tabActiveText: "#333",
      tabActiveRing: "rgba(0,0,0,0.06)",
      dragBarBg: "#e8e8e8",
      dragHandle: "#ccc",
      border: "#e0e0e0",
      menuBg: "#ffffff",
      menuText: "#333",
      menuHover: "#e8e8e8",
      hintText: "rgba(0,0,0,0.35)",
    },
  },
};

export function applyThemeToCSS(theme: Theme) {
  const root = document.documentElement;
  const c = theme.chrome;
  root.style.setProperty("--panel-bg", c.panelBg);
  root.style.setProperty("--tab-bar-bg", c.tabBarBg);
  root.style.setProperty("--tab-active-bg", c.tabActiveBg);
  root.style.setProperty("--tab-hover-bg", c.tabHoverBg);
  root.style.setProperty("--tab-text", c.tabText);
  root.style.setProperty("--tab-hover-text", c.tabHoverText);
  root.style.setProperty("--tab-active-text", c.tabActiveText);
  root.style.setProperty("--tab-active-ring", c.tabActiveRing);
  root.style.setProperty("--drag-bar-bg", c.dragBarBg);
  root.style.setProperty("--drag-handle", c.dragHandle);
  root.style.setProperty("--border-color", c.border);
  root.style.setProperty("--menu-bg", c.menuBg);
  root.style.setProperty("--menu-text", c.menuText);
  root.style.setProperty("--menu-hover", c.menuHover);
  root.style.setProperty("--hint-text", c.hintText);
  root.style.setProperty("--terminal-bg", theme.terminal.background);
}

export function getTerminalTheme(theme: Theme) {
  return {
    background: theme.terminal.background,
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
