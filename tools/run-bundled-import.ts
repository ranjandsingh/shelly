#!/usr/bin/env bun
// One-shot: read bundled-themes.urls.txt, run parseVSCodeTheme on each,
// write src/lib/themes/bundled/<slug>.ts, print a summary.
import { parseVSCodeTheme, validateTheme } from "../src/lib/themes/parse";
import { readFileSync, writeFileSync, existsSync, mkdirSync } from "node:fs";
import { join } from "node:path";

const urlsFile = "tools/bundled-themes.urls.txt";
const outDir = "src/lib/themes/bundled";
if (!existsSync(outDir)) mkdirSync(outDir, { recursive: true });

const lines = readFileSync(urlsFile, "utf-8").split(/\r?\n/);
const entries: { url: string; slug: string }[] = [];
for (const raw of lines) {
  const line = raw.trim();
  if (!line || line.startsWith("#")) continue;
  const [url, slug] = line.split("|").map((s) => s.trim());
  if (!url) continue;
  entries.push({ url, slug: slug || "" });
}

const ok: string[] = [];
const fail: { slug: string; url: string; reason: string }[] = [];

for (const { url, slug: slugArg } of entries) {
  try {
    const res = await fetch(url);
    if (!res.ok) {
      fail.push({ slug: slugArg || url, url, reason: `HTTP ${res.status}` });
      continue;
    }
    const text = await res.text();
    const theme = parseVSCodeTheme(text);
    validateTheme(theme);
    const slug = slugArg || theme.name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
    // Build a valid JS identifier from the slug: strip non-alphanumeric,
    // camelCase on word boundaries, leading char must be a letter.
    const camel = slug.replace(/-+([a-z0-9])/g, (_, c: string) => c.toUpperCase());
    const varName = /^[a-z]/.test(camel) ? camel : "theme" + camel.charAt(0).toUpperCase() + camel.slice(1);
    const content = `import type { Theme } from "../../themes";\n\nexport const ${varName}: Theme = ${JSON.stringify(theme, null, 2)};\n`;
    writeFileSync(join(outDir, `${slug}.ts`), content, "utf-8");
    ok.push(slug);
    console.log(`ok   ${slug}`);
  } catch (e: any) {
    fail.push({ slug: slugArg || url, url, reason: e?.message ?? String(e) });
    console.log(`fail ${slugArg || url}: ${e?.message ?? e}`);
  }
}

console.log(`\n== summary ==`);
console.log(`ok:   ${ok.length}`);
console.log(`fail: ${fail.length}`);
if (fail.length) {
  console.log(`\nfailures:`);
  for (const f of fail) console.log(`  ${f.slug}: ${f.reason}`);
}
