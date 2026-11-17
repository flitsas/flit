"use client";

import type { OverrideScope, ResolutionLevel } from "@/lib/api/types-documents";
import { ETIQUETA_PUNTO_CLS, etiquetaKit, type StatusBadgeTone } from "@flit/ui/StatusBadge";

// Badge de nivel/scope documental (HU #10198, AC3/AC4/AC5). Colores con contraste
// AA: CLIENTE (azul), OT (tinta), DEFAULT (gris), con los tonos de badge del kit (HU #14510). Reutilizado por el formulario
// de overrides, las listas de overrides y la matriz resuelta.
type BadgeLevel = OverrideScope | ResolutionLevel;

const STYLES: Record<BadgeLevel, { tono: StatusBadgeTone; label: string }> = {
  CLIENTE: { tono: "info", label: "CLIENTE" },
  OT: { tono: "tinta", label: "OT" },
  DEFAULT: { tono: "neutral", label: "DEFAULT" },
};

export function ScopeBadge({ scope }: { scope: BadgeLevel }) {
  const style = STYLES[scope];
  return (
    <span {...etiquetaKit(style.tono, { className: "uppercase tracking-wide" })}>
      <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
      {style.label}
    </span>
  );
}
