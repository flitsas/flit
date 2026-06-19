"use client";

import { Trash2 } from "lucide-react";
import { ScopeBadge } from "@/components/admin/documents/panels/ScopeBadge";
import type { DocumentOrderOverride } from "@/lib/api/types-documents";

// Lista de overrides de orden de una combinación (trámite, scope, referencia)
// (HU #10198, AC3/AC4). Cada fila muestra el badge del scope (OT/CLIENTE), el orden
// y el documento, con acción de eliminar. Presentacional: la baja se delega.
export interface OverridesListProps {
  overrides: DocumentOrderOverride[];
  onDelete: (override: DocumentOrderOverride) => void;
  busy?: boolean;
}

export function OverridesList({ overrides, onDelete, busy }: OverridesListProps) {
  return (
    <ul className="flex flex-col gap-2" aria-label="Overrides de orden">
      {overrides.map((o) => (
        <li
          key={o.id}
          className="flex items-center gap-3 rounded-xl border bg-white px-3 py-2.5 dark:bg-[#0B0F14]"
          style={{ borderColor: "#DFE5ED" }}
        >
          <ScopeBadge scope={o.scope} />
          <span
            className="flex h-6 w-6 shrink-0 items-center justify-center rounded-lg text-[10px] font-bold text-white"
            style={{ background: "#557EFF" }}
            aria-label={`Orden ${o.orden}`}
          >
            {o.orden}
          </span>
          <div className="min-w-0 flex-1">
            <p className="truncate text-xs font-semibold">{o.documento.nombre}</p>
            <p className="truncate font-mono text-[10px] opacity-60">{o.documento.codigo}</p>
          </div>
          <button
            type="button"
            onClick={() => onDelete(o)}
            disabled={busy}
            aria-label={`Eliminar override de ${o.documento.nombre}`}
            className="rounded-lg border p-1 disabled:opacity-40"
            style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
          >
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        </li>
      ))}
    </ul>
  );
}
