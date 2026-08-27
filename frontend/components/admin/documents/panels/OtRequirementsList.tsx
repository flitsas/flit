"use client";

import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import {
  REQUIREMENT_SELECTION_LABELS,
  type DocumentRequirementSelection,
  type DocumentType,
} from "@/lib/api/types-documents";
import { catalogDocumentName, catalogDocumentTitle } from "@/lib/tramites/document-labels";
import { DocumentCatalogCaption } from "@/components/shared/DocumentCatalogCaption";

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
  const [query, setQuery] = useState("");
  const filtered = useMemo(() => {
    const q = foldSearch(query);
    if (!q) return documents;
    return documents.filter((d) => {
      const label = catalogDocumentName(d.codigo, d.nombre);
      return foldSearch(label).includes(q) || foldSearch(d.codigo).includes(q) || foldSearch(d.nombre).includes(q);
    });
  }, [documents, query]);

  if (documents.length === 0) {
    return (
      <p className="rounded-2xl border p-6 text-center text-xs opacity-60">
        Agrega documentos en «Orden de los documentos» para definir su obligatoriedad por OT.
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2 rounded-xl border bg-white px-3 py-2 dark:bg-[#0B0F14]">
        <Search className="h-4 w-4 shrink-0 opacity-60" aria-hidden="true" />
        <label htmlFor="ot-req-doc-search" className="sr-only">
          Buscar documento
        </label>
        <input
          id="ot-req-doc-search"
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Buscar documento por nombre o código…"
          className="w-full bg-transparent text-xs outline-none"
        />
      </div>
      {filtered.length === 0 ? (
        <p className="rounded-2xl border p-6 text-center text-xs opacity-60">
          Ningún documento coincide con la búsqueda.
        </p>
      ) : (
    <ul className="flex flex-col gap-2" aria-label="Obligatoriedad por organismo de tránsito">
      {filtered.map((d) => {
        const selection = selectionByDocId[d.id] ?? "DEFAULT";
        const label = catalogDocumentTitle(d.codigo, d.nombre);
        return (
          <li
            key={d.id}
            className="flex items-center gap-3 rounded-xl border bg-white px-3 py-2.5 dark:bg-[#0B0F14]"
          >
            <div className="min-w-0 flex-1">
              <p className="truncate text-xs font-semibold">
                <DocumentCatalogCaption nombre={d.nombre} codigo={d.codigo} />
              </p>
            </div>
            <select
              aria-label={`Obligatoriedad de ${label}`}
              value={selection}
              disabled={busy}
              onChange={(e) => onChange(d.id, e.target.value as DocumentRequirementSelection)}
              className="rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 disabled:opacity-50"
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
      )}
    </div>
  );
}

function foldSearch(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "");
}
