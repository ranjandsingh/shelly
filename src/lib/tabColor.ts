export const NAMED_COLORS: Record<string, string> = {
  red: "#ef4444",
  orange: "#f97316",
  yellow: "#eab308",
  green: "#22c55e",
  cyan: "#06b6d4",
  blue: "#3b82f6",
  purple: "#a855f7",
  pink: "#ec4899",
};

export const PALETTE_ORDER = [
  "red",
  "orange",
  "yellow",
  "green",
  "cyan",
  "blue",
  "purple",
  "pink",
] as const;

function hashString(s: string): number {
  let h = 2166136261 >>> 0;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 16777619) >>> 0;
  }
  return h;
}

export function pathToHue(path: string): number {
  return hashString(path.toLowerCase()) % 360;
}

/**
 * Resolve the tint color for a given path.
 * Returns a CSS color string, or null for "no tint".
 *
 * Priority:
 *  1. Explicit override (named color) -> NAMED_COLORS[...]
 *  2. Explicit override === "none"    -> null
 *  3. Duplicate auto-tint when groupSize >= 2 -> hsl(hash, 65%, 55%)
 *  4. Solo tab                         -> null
 */
export function resolveTabColor(
  path: string,
  groupSize: number,
  overrides: Record<string, string>,
): string | null {
  const override = overrides[path];
  if (override && override !== "none" && override !== "auto") {
    return NAMED_COLORS[override] ?? null;
  }
  if (override === "none") return null;
  if (groupSize >= 2) return `hsl(${pathToHue(path)}, 65%, 55%)`;
  return null;
}
