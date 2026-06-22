"use client";

import {
  REQUIREMENT_SELECTION_LABELS,
  type DocumentRequirementSelection,
  type DocumentType,
} from "@/lib/api/types-documents";

// Obligatoriedad documental por Organismo de Tránsito (HU #10198) — granular SOLO para OT.
// Por cada documento asociado al trámite, un selector de 4 opciones: «Por defecto» (hereda
// del trámite), Obligatorio, Opcional o No aplica (oculta el documento de la matriz de ese
// OT). Presentacional: la carga y la persistencia las gestiona el contenedor (tab OT).
const SELECTION_ORDER: DocumentRequirementSelection[] = [
  "DEFAULT",
  "REQUIRED",
  "OPTIONAL",
  "NOT_APPLICABLE",
];

export interface OtRequirementsListProps {
  documents: DocumentType[];
  /** Selección efectiva por documento; la ausencia se trata como «DEFAULT». */
  selectionByDocId: Record<string, DocumentRequirementSelection>;
  onChange: (documentTypeId: string, selection: DocumentRequirementSelection) => void;
  busy?: boolean;
}

export function OtRequirementsList({
  documents,
  selectionByDocId,
  onChange,
  busy,
}: OtRequirementsListProps) {
  if (documents.length === 0) {
    return (
      <p className="rounded-2xl border p-6 text-center text-xs opacity-60" style={{ borderColor: "#DFE5ED" }}>
        Agrega documentos en «Orden de los documentos» para definir su obligatoriedad por OT.
      </p>
    );
  }

  return (
    <ul className="flex flex-col gap-2" aria-label="Obligatoriedad por organismo de tránsito">
      {documents.map((d) => {
        const selection = selectionByDocId[d.id] ?? "DEFAULT";
        return (
          <li
            key={d.id}
            className="flex items-center gap-3 rounded-xl border bg-white px-3 py-2.5 dark:bg-[#0B0F14]"
            style={{ borderColor: "#DFE5ED" }}
          >
            <div className="min-w-0 flex-1">
              <p className="truncate text-xs font-semibold">{d.nombre}</p>
              <p className="truncate font-mono text-[10px] opacity-60">{d.codigo}</p>
            </div>
            <select
              aria-label={`Obligatoriedad de ${d.nombre}`}
              value={selection}
              disabled={busy}
              onChange={(e) => onChange(d.id, e.target.value as DocumentRequirementSelection)}
              className="rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 disabled:opacity-50"
              style={{ borderColor: "#DFE5ED" }}
            >
              {SELECTION_ORDER.map((value) => (
                <option key={value} value={value}>
                  {REQUIREMENT_SELECTION_LABELS[value]}
                </option>
              ))}
            </select>
          </li>
        );
      })}
    </ul>
  );
}
