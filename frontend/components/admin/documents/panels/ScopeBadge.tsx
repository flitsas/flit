"use client";

import type { OverrideScope, ResolutionLevel } from "@/lib/api/types-documents";

// Badge de nivel/scope documental (HU #10198, AC3/AC4/AC5). Colores con contraste
// AA: CLIENTE (azul), OT (turquesa), DEFAULT (gris). Reutilizado por el formulario
// de overrides, las listas de overrides y la matriz resuelta.
type BadgeLevel = OverrideScope | ResolutionLevel;

const STYLES: Record<BadgeLevel, { bg: string; fg: string; label: string }> = {
  CLIENTE: { bg: "#557EFF", fg: "#FFFFFF", label: "CLIENTE" },
  OT: { bg: "#00DBD5", fg: "#0a3d3b", label: "OT" },
  DEFAULT: { bg: "#DFE5ED", fg: "#162744", label: "DEFAULT" },
};

export function ScopeBadge({ scope }: { scope: BadgeLevel }) {
  const style = STYLES[scope];
  return (
    <span
      className="inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide"
      style={{ background: style.bg, color: style.fg }}
    >
      {style.label}
    </span>
  );
}
