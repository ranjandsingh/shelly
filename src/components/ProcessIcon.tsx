import { 
  siClaude, 
  siNodedotjs, 
  siBun, 
  siPython, 
  siDeno, 
  siRuby, 
  siGo, 
  siRust, 
  siCursor,
  type SimpleIcon
} from "simple-icons";

export const PROCESS_ICONS: Record<string, SimpleIcon> = {
  claude: siClaude,
  node: siNodedotjs,
  bun: siBun,
  python: siPython,
  deno: siDeno,
  ruby: siRuby,
  go: siGo,
  rust: siRust,
  cursor: siCursor,
};

export function ProcessIcon({ process, size = 14 }: { process: string; size?: number }) {
  const icon = PROCESS_ICONS[process];
  if (!icon) return null;
  return (
    <svg
      className="proc-icon"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill={`#${icon.hex}`}
      aria-label={process}
      style={{ flexShrink: 0 }}
    >
      <path d={icon.path} />
      <title>{process}</title>
    </svg>
  );
}
