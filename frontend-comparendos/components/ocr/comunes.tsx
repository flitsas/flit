"use client";

import { useEffect } from "react";
import { ESTADO_DOCUMENTO, progreso, type ContadoresPaquete } from "@/lib/ocr";

/** Chip de estado de un documento OCR con los colores de §7.6 (incluido el morado de DUPLICADO). */
export function EstadoDocumento({ estado }: { estado: string }) {
  const e = ESTADO_DOCUMENTO[estado] ?? { texto: estado, clase: "bg-[#EEF1F6] text-[#475569]" };
  return <span className={`inline-flex items-center whitespace-nowrap rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${e.clase}`}>{e.texto}</span>;
}

/** Barra de avance «45/50 procesados». */
export function Progreso({ p }: { p: ContadoresPaquete }) {
  const r = progreso(p);
  return (
    <div className="flex min-w-40 items-center gap-2 text-xs">
      <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-[#EEF1F6] dark:bg-white/10">
        <div className="h-full rounded-full bg-[#557EFF]" style={{ width: `${r.porcentaje}%` }} />
      </div>
      <span className="whitespace-nowrap tabular-nums opacity-70">{r.texto}</span>
    </div>
  );
}

/** Cifra con su rótulo, para las filas de contadores. */
export function Contador({ etiqueta, valor, clase }: { etiqueta: string; valor: number | undefined; clase?: string }) {
  return (
    <div className="flex min-w-24 flex-col gap-0.5 rounded-xl border border-[#DFE5ED] px-3 py-2 dark:border-white/15">
      <span className="text-[11px] font-semibold text-[#5B6B82] dark:text-slate-300">{etiqueta}</span>
      <span className={`text-lg font-semibold tabular-nums ${clase ?? "text-[#162744] dark:text-white"}`}>{valor ?? 0}</span>
    </div>
  );
}

/** Mientras haya algo en cola se vuelve a consultar cada pocos segundos. */
export function useSondeo(activo: boolean, recargar: () => void, ms = 4000) {
  useEffect(() => {
    if (!activo) return;
    const t = setInterval(recargar, ms);
    return () => clearInterval(t);
  }, [activo, recargar, ms]);
}
