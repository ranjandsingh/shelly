import type { Theme } from "../../themes";
import { vsDark } from "./vs-dark";
import { monokai } from "./monokai";
import { dracula } from "./dracula";
import { solarizedDark } from "./solarized-dark";
import { oneDark } from "./one-dark";
import { vsLight } from "./vs-light";

export const BUILTIN_THEMES: Record<string, Theme> = {
  "vs-dark": vsDark,
  "monokai": monokai,
  "dracula": dracula,
  "solarized-dark": solarizedDark,
  "one-dark": oneDark,
  "vs-light": vsLight,
};
