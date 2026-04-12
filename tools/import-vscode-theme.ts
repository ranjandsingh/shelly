#!/usr/bin/env bun
/**
 * Usage: bun tools/import-vscode-theme.ts <url> [slug]
 * Fetches a VS Code color-theme JSON file (raw.githubusercontent.com),
 * maps it via parseVSCodeTheme, and prints a ready-to-paste ts module.
 */
import { parseVSCodeTheme, validateTheme } from "../src/lib/themes/parse";

async function main() {
  const url = process.argv[2];
  const slugArg = process.argv[3];
  if (!url) {
    console.error("usage: bun tools/import-vscode-theme.ts <url> [slug]");
    process.exit(1);
  }
  const res = await fetch(url);
  if (!res.ok) {
    console.error(`fetch failed: ${res.status} ${res.statusText}`);
    process.exit(1);
  }
  const text = await res.text();
  const theme = parseVSCodeTheme(text);
  validateTheme(theme);
  const slug = slugArg ?? theme.name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
  const varName = slug.replace(/-([a-z])/g, (_, c) => c.toUpperCase());

  const out = `// src/lib/themes/bundled/${slug}.ts
import type { Theme } from "../../themes";

export const ${varName}: Theme = ${JSON.stringify(theme, null, 2)
    .replace(/"([^"]+)":/g, "$1:")
    .replace(/"#([0-9a-fA-F]{6,8})"/g, '"#$1"')};
`;
  console.log(out);
  console.error(`# slug: ${slug}`);
  console.error(`# var:  ${varName}`);
}

main().catch((e) => {
  console.error(String(e));
  process.exit(1);
});
