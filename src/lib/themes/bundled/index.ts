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
import { palenight } from "./palenight";
import { vitesseDark } from "./vitesse-dark";
import { vitesseDarkSoft } from "./vitesse-dark-soft";
import { vitesseBlack } from "./vitesse-black";
import { horizon } from "./horizon";
import { everforestDark } from "./everforest-dark";
import { gruvboxMaterialDark } from "./gruvbox-material-dark";

import { ayuLight } from "./ayu-light";
import { winterIsComingLight } from "./winter-is-coming-light";
import { vitesseLight } from "./vitesse-light";
import { vitesseLightSoft } from "./vitesse-light-soft";
import { horizonBright } from "./horizon-bright";
import { everforestLight } from "./everforest-light";
import { gruvboxMaterialLight } from "./gruvbox-material-light";

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
  "palenight": palenight,
  "vitesse-dark": vitesseDark,
  "vitesse-dark-soft": vitesseDarkSoft,
  "vitesse-black": vitesseBlack,
  "horizon": horizon,
  "everforest-dark": everforestDark,
  "gruvbox-material-dark": gruvboxMaterialDark,
  "blueberry-banana": blueberryBanana,
  "vs-light": vsLight,
  "ayu-light": ayuLight,
  "vitesse-light": vitesseLight,
  "vitesse-light-soft": vitesseLightSoft,
  "winter-is-coming-light": winterIsComingLight,
  "horizon-bright": horizonBright,
  "everforest-light": everforestLight,
  "gruvbox-material-light": gruvboxMaterialLight,
};
