"use client";

import { X } from "lucide-react";
import type { ClaveEtiqueta, EtiquetaFiltro } from "@/lib/filtros";

/**
 * HU #13868: la tira de lo que está filtrando la bandeja ahora (lo aplicado, no lo que se está armando en el menú), como
 * la de Trámites. «Activos» lleva borde punteado porque viene de fábrica, pero se quita con la × igual que las demás.
 */
export function EtiquetasFiltros({
  etiquetas,
  onQuitar,
  onLimpiarTodo,
}: {
  etiquetas: EtiquetaFiltro[];
  onQuitar: (clave: ClaveEtiqueta) => void;
  onLimpiarTodo?: () => void;
}) {
  if (etiquetas.length === 0 && !onLimpiarTodo) return null;
  return (
    <div className="flex flex-wrap items-center gap-2" aria-label="Filtros aplicados" role="group">
      {etiquetas.map((e) => (
        <span
          key={e.clave}
          className={`flex items-center gap-1.5 rounded-full border border-flit-brand bg-[#EEF5FF] px-2.5 py-1 text-xs font-semibold text-[#3B4FD6] dark:bg-flit-brand/10 dark:text-[#8FA8FF] ${
            e.porDefecto ? "border-dashed" : ""
          }`}
          title={e.porDefecto ? "Filtro por defecto de la bandeja" : undefined}
        >
          {e.texto}
          <button
            type="button"
            onClick={() => onQuitar(e.clave)}
            aria-label={`Quitar filtro ${e.texto}`}
            className="rounded-full transition hover:opacity-70 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2"
          >
            <X className="h-3 w-3" aria-hidden />
          </button>
        </span>
      ))}
      {onLimpiarTodo && (
        <button
          type="button"
          onClick={onLimpiarTodo}
          className="rounded-full px-2 py-1 text-xs font-semibold text-[#59677D] underline-offset-2 transition hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand dark:text-slate-300"
        >
          Limpiar todo
        </button>
      )}
    </div>
  );
}
