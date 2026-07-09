"use client";

import { FileText, Trash2 } from "lucide-react";
import type { ProcedureDocumentRequirement } from "@/lib/api/types-documents";

// Lista de documentos asociados a un trámite (HU #10198; RF22). Define QUÉ documentos
// exige el trámite y cuáles son obligatorios. El ORDEN ya no se configura aquí: tras RF22
// el único nivel que reordena documentos es el Organismo de Tránsito (pestaña «Overrides
// OT»). El toggle de obligatoriedad y la baja se delegan al contenedor (persistencia API).
export interface DocumentAssociationListProps {
  items: ProcedureDocumentRequirement[];
  onToggleObligatorio: (item: ProcedureDocumentRequirement) => void;
  onRemove: (item: ProcedureDocumentRequirement) => void;
  /** Deshabilita interacciones mientras hay una operación en vuelo. */
  busy?: boolean;
}

export function DocumentAssociationList({
  items,
  onToggleObligatorio,
  onRemove,
  busy,
}: DocumentAssociationListProps) {
  return (
    <ul className="flex flex-col gap-2" aria-label="Documentos asociados al trámite">
      {items.map((item) => (
        <li
          key={item.id}
          className="flex items-center gap-3 rounded-xl border bg-white px-3 py-2.5 dark:bg-[#0B0F14]"
        >
          <span
            className="flex h-6 w-6 shrink-0 items-center justify-center rounded-lg text-white"
            style={{ background: "#557EFF" }}
            aria-hidden="true"
          >
            <FileText className="h-3.5 w-3.5" />
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
        </li>
      ))}
    </ul>
  );
}
