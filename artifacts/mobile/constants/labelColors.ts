export interface LabelColor {
  id: string;
  hex: string;
  name: string;
}

export const LABEL_COLORS: LabelColor[] = [
  { id: "red",    hex: "#ef4444", name: "红色" },
  { id: "orange", hex: "#f97316", name: "橙色" },
  { id: "amber",  hex: "#f59e0b", name: "黄色" },
  { id: "green",  hex: "#22c55e", name: "绿色" },
  { id: "teal",   hex: "#14b8a6", name: "青色" },
  { id: "blue",   hex: "#4f7ef8", name: "蓝色" },
  { id: "purple", hex: "#8b5cf6", name: "紫色" },
  { id: "pink",   hex: "#ec4899", name: "粉色" },
  { id: "slate",  hex: "#64748b", name: "灰色" },
];

/** Returns the hex for a color id, falling back to the app primary. */
export function resolveColor(colorId: string | undefined, fallback: string): string {
  if (!colorId) return fallback;
  return LABEL_COLORS.find((c) => c.id === colorId)?.hex ?? fallback;
}
