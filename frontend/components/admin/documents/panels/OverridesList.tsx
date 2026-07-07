"use client";

import { useState } from "react";
import { ArrowDown, ArrowUp, GripVertical, Trash2 } from "lucide-react";
import { ScopeBadge } from "@/components/admin/documents/panels/ScopeBadge";
import type { DocumentOrderOverride } from "@/lib/api/types-documents";

// Lista de overrides de orden de una combinación (trámite, scope, referencia)
// (HU #10198, AC3/AC4). Reordenable por arrastre (drag-and-drop HTML5) y, como
// alternativa accesible WCAG, botones ↑/↓ por fila — igual que la lista de documentos
// del trámite. Ambos caminos emiten el nuevo orden completo vía `onReorder`. La baja se
// delega al contenedor. Cada fila muestra el badge del scope (OT/CLIENTE) y el orden.
export interface OverridesListProps {
  overrides: DocumentOrderOverride[];
  /** Recibe la lista reordenada (mismo conjunto, distinto orden) para persistir. */
  onReorder: (ordered: DocumentOrderOverride[]) => void;
  onDelete: (override: DocumentOrderOverride) => void;
  busy?: boolean;
}

export function OverridesList({ overrides, onReorder, onDelete, busy }: OverridesListProps) {
  const [dragIndex, setDragIndex] = useState<number | null>(null);

  const move = (from: number, to: number) => {
    if (to < 0 || to >= overrides.length || from === to) {
      return;
    }
    const next = [...overrides];
    const [moved] = next.splice(from, 1);
    next.splice(to, 0, moved);
    onReorder(next);
  };

  return (
    <ul className="flex flex-col gap-2" aria-label="Overrides de orden">
      {overrides.map((o, index) => (
        <li
          key={o.id}
          draggable={!busy}
          onDragStart={() => setDragIndex(index)}
          onDragOver={(e) => e.preventDefault()}
          onDrop={(e) => {
            e.preventDefault();
            if (dragIndex !== null) {
              move(dragIndex, index);
            }
            setDragIndex(null);
          }}
          onDragEnd={() => setDragIndex(null)}
          className="flex items-center gap-3 rounded-xl border bg-white px-3 py-2.5 dark:bg-[#0B0F14]"
        >
          <span className="cursor-grab text-slate-400" aria-hidden="true">
            <GripVertical className="h-4 w-4" />
          </span>

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

          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => move(index, index - 1)}
              disabled={busy || index === 0}
              aria-label={`Subir ${o.documento.nombre}`}
              className="rounded-lg border p-1 disabled:opacity-30"
            >
              <ArrowUp className="h-3.5 w-3.5" />
            </button>
            <button
              type="button"
              onClick={() => move(index, index + 1)}
              disabled={busy || index === overrides.length - 1}
              aria-label={`Bajar ${o.documento.nombre}`}
              className="rounded-lg border p-1 disabled:opacity-30"
            >
              <ArrowDown className="h-3.5 w-3.5" />
            </button>
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
          </div>
        </li>
      ))}
    </ul>
  );
}
