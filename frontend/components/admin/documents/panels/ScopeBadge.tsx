"use client";

import type { OverrideScope, ResolutionLevel } from "@/lib/api/types-documents";

// Badge de nivel/scope documental (HU #10198, AC3/AC4/AC5). Colores con contraste
// AA: CLIENTE (azul), OT (tinta), DEFAULT (gris), con los tonos de badge del kit (HU #14510). Reutilizado por el formulario
// de overrides, las listas de overrides y la matriz resuelta.
type BadgeLevel = OverrideScope | ResolutionLevel;

const STYLES: Record<BadgeLevel, { bg: string; fg: string; label: string }> = {
  CLIENTE: { bg: "var(--badge-info-bg)", fg: "var(--badge-info-fg)", label: "CLIENTE" },
  OT: { bg: "var(--badge-tinta-bg)", fg: "var(--badge-tinta-fg)", label: "OT" },
  DEFAULT: { bg: "var(--badge-neutral-bg)", fg: "var(--badge-neutral-fg)", label: "DEFAULT" },
};

export function ScopeBadge({ scope }: { scope: BadgeLevel }) {
  const style = STYLES[scope];
  return (
    <span
      className="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-bold uppercase tracking-wide"
      style={{ background: style.bg, color: style.fg }}
    >
      {style.label}
    </span>
  );
}
