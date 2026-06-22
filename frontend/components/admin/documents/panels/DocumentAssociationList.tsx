"use client";

import { useState } from "react";
import { ArrowDown, ArrowUp, GripVertical, Trash2 } from "lucide-react";
import type { ProcedureDocumentRequirement } from "@/lib/api/types-documents";

// Lista reordenable de documentos asociados a un trámite (HU #10198, AC2). Soporta
// drag-and-drop (HTML5) y, como alternativa accesible WCAG, botones ↑/↓ por fila.
// Ambos caminos emiten el nuevo orden completo vía `onReorder`. El toggle de
// obligatoriedad y la baja se delegan al contenedor (persistencia vía API).
export interface DocumentAssociationListProps {
  items: ProcedureDocumentRequirement[];
  /** Recibe la lista reordenada (mismo conjunto, distinto orden) para persistir. */
  onReorder: (ordered: ProcedureDocumentRequirement[]) => void;
  onToggleObligatorio: (item: ProcedureDocumentRequirement) => void;
  onRemove: (item: ProcedureDocumentRequirement) => void;
  /** Deshabilita interacciones mientras hay una operación en vuelo. */
  busy?: boolean;
}

export function DocumentAssociationList({
  items,
  onReorder,
  onToggleObligatorio,
  onRemove,
  busy,
}: DocumentAssociationListProps) {
  const [dragIndex, setDragIndex] = useState<number | null>(null);

  const move = (from: number, to: number) => {
    if (to < 0 || to >= items.length || from === to) {
      return;
    }
    const next = [...items];
    const [moved] = next.splice(from, 1);
    next.splice(to, 0, moved);
    onReorder(next);
  };

  return (
    <ul className="flex flex-col gap-2" aria-label="Documentos asociados al trámite">
      {items.map((item, index) => (
        <li
          key={item.id}
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
          style={{ borderColor: "#DFE5ED" }}
        >
          <span className="cursor-grab text-slate-400" aria-hidden="true">
            <GripVertical className="h-4 w-4" />
          </span>

          <span
            className="flex h-6 w-6 shrink-0 items-center justify-center rounded-lg text-[10px] font-bold text-white"
            style={{ background: "#557EFF" }}
            aria-label={`Orden ${index + 1}`}
          >
            {index + 1}
          </span>

          <div className="min-w-0 flex-1">
            <p className="truncate text-xs font-semibold">{item.documento.nombre}</p>
            <p className="truncate font-mono text-[10px] opacity-60">{item.documento.codigo}</p>
          </div>

          <label className="flex items-center gap-1.5 text-[10px] font-semibold">
            <input
              type="checkbox"
              checked={item.obligatorio}
              disabled={busy}
              onChange={() => onToggleObligatorio(item)}
              aria-label={`Documento obligatorio: ${item.documento.nombre}`}
            />
            Obligatorio
          </label>

          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => move(index, index - 1)}
              disabled={busy || index === 0}
              aria-label={`Subir ${item.documento.nombre}`}
              className="rounded-lg border p-1 disabled:opacity-30"
              style={{ borderColor: "#DFE5ED" }}
            >
              <ArrowUp className="h-3.5 w-3.5" />
            </button>
            <button
              type="button"
              onClick={() => move(index, index + 1)}
              disabled={busy || index === items.length - 1}
              aria-label={`Bajar ${item.documento.nombre}`}
              className="rounded-lg border p-1 disabled:opacity-30"
              style={{ borderColor: "#DFE5ED" }}
            >
              <ArrowDown className="h-3.5 w-3.5" />
            </button>
            <button
              type="button"
              onClick={() => onRemove(item)}
              disabled={busy}
              aria-label={`Remover ${item.documento.nombre}`}
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
