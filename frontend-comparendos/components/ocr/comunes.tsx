"use client";

import { useEffect } from "react";
import { StatusBadge, type StatusBadgeTone } from "@flit/ui";
import { ESTADO_DOCUMENTO, progreso, type ContadoresPaquete } from "@/lib/ocr";

/** Tono del kit por estado de documento (§7.6, incluido el morado de DUPLICADO). */
export const TONO_DOCUMENTO: Record<string, StatusBadgeTone> = {
  Pendiente: "neutral",
  EnProcesamiento: "info",
  Completo: "success",
  Incompleto: "warning",
  Fallido: "danger",
  Duplicado: "purple",
  Registrado: "success",
  Asociado: "info",
  PorAsociar: "info",
  Descartado: "neutral",
};

/** Color de la cifra de una tarjeta de contador, con el mismo tono que el chip del estado. */
export const COLOR_CIFRA_DOCUMENTO: Record<string, string> = {
  Completo: "var(--flit-success-ink)",
  Incompleto: "var(--badge-warning-fg)",
  Fallido: "var(--badge-danger-fg)",
  Duplicado: "var(--badge-purple-fg)",
  Asociado: "var(--badge-info-fg)",
  PorAsociar: "var(--badge-info-fg)",
};

/** Chip de estado de un documento OCR con el StatusBadge del kit. */
export function EstadoDocumento({ estado }: { estado: string }) {
  return <StatusBadge label={ESTADO_DOCUMENTO[estado]?.texto ?? estado} tone={TONO_DOCUMENTO[estado] ?? "neutral"} />;
}

/** Barra de avance «45/50 procesados». */
export function Progreso({ p }: { p: ContadoresPaquete }) {
  const r = progreso(p);
  return (
    <div className="flex min-w-40 items-center gap-2 text-xs">
      <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-secondary">
        <div className="h-full rounded-full bg-flit-brand" style={{ width: `${r.porcentaje}%` }} />
      </div>
      <span className="whitespace-nowrap tabular-nums text-muted-foreground">{r.texto}</span>
    </div>
  );
}

/** Rejilla de tarjetas de contador: 2 columnas en celular, y desde sm las del kit (`.metricas`) algo más angostas. */
export const CONTADORES_GRID_CLS = "grid grid-cols-2 gap-3 sm:grid-cols-[repeat(auto-fit,minmax(150px,1fr))]";

/** Mientras haya algo en cola se vuelve a consultar cada pocos segundos. */
export function useSondeo(activo: boolean, recargar: () => void, ms = 4000) {
  useEffect(() => {
    if (!activo) return;
    const t = setInterval(recargar, ms);
    return () => clearInterval(t);
  }, [activo, recargar, ms]);
}
