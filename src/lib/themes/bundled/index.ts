import type { Theme } from "../../themes";

import { vsDark } from "./vs-dark";
import { monokai } from "./monokai";
import { dracula } from "./dracula";
import { solarizedDark } from "./solarized-dark";
import { oneDark } from "./one-dark";
import { vsLight } from "./vs-light";

import { blueberryBanana } from "./blueberry-banana";

import { nightOwl } from "./night-owl";
import { tokyoNight } from "./tokyo-night";
import { tokyoNightStorm } from "./tokyo-night-storm";
import { nord } from "./nord";
import { ayuDark } from "./ayu-dark";
import { ayuMirage } from "./ayu-mirage";
import { synthwave84 } from "./synthwave-84";
import { shadesOfPurple } from "./shades-of-purple";
import { cobalt2 } from "./cobalt2";
import { andromeda } from "./andromeda";
import { winterIsComingDark } from "./winter-is-coming-dark";
import { panda } from "./panda";

import { ayuLight } from "./ayu-light";
import { winterIsComingLight } from "./winter-is-coming-light";

export const BUILTIN_THEMES: Record<string, Theme> = {
  "vs-dark": vsDark,
  "one-dark": oneDark,
  "monokai": monokai,
  "dracula": dracula,
  "night-owl": nightOwl,
  "tokyo-night": tokyoNight,
  "tokyo-night-storm": tokyoNightStorm,
  "nord": nord,
  "ayu-dark": ayuDark,
  "ayu-mirage": ayuMirage,
  "solarized-dark": solarizedDark,
  "synthwave-84": synthwave84,
  "shades-of-purple": shadesOfPurple,
  "cobalt2": cobalt2,
  "andromeda": andromeda,
  "winter-is-coming-dark": winterIsComingDark,
  "panda": panda,
  "blueberry-banana": blueberryBanana,
  "vs-light": vsLight,
  "ayu-light": ayuLight,
  "winter-is-coming-light": winterIsComingLight,
};
