"use client";

import { Search, X } from "lucide-react";
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from "@/lib/validation/fieldRules";

export interface DocumentTypeCatalogFilters {
  q: string;
  origen: "" | "cargue" | "autogenerado";
  estado: "" | "activo" | "inactivo";
}

export const EMPTY_DOCUMENT_TYPE_FILTERS: DocumentTypeCatalogFilters = {
  q: "",
  origen: "",
  estado: "",
};

const INPUT_CLS =
  "w-full rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF] border-[#DFE5ED]";

export function DocumentTypeFiltersBar({
  value,
  onChange,
  onApply,
  onClear,
}: {
  value: DocumentTypeCatalogFilters;
  onChange: (next: DocumentTypeCatalogFilters) => void;
  onApply: (next?: DocumentTypeCatalogFilters) => void;
  onClear: () => void;
}) {
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        onApply();
      }}
      aria-label="Filtros del catálogo documental"
      className="mb-4 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
    >
      <label className="block text-xs font-semibold" htmlFor="doc-filter-q">
        Buscar
        <input
          id="doc-filter-q"
          value={value.q}
          onChange={(e) =>
            onChange({ ...value, q: sanitizeNoAngleBrackets(e.target.value) })
          }
          maxLength={SEARCH_TEXT_MAX_LENGTH}
          placeholder="Código o nombre"
          className={`${INPUT_CLS} mt-1`}
        />
      </label>
      <label className="block text-xs font-semibold" htmlFor="doc-filter-origen">
        Origen
        <select
          id="doc-filter-origen"
          value={value.origen}
          onChange={(e) => {
            const origen = e.target.value as DocumentTypeCatalogFilters["origen"];
            const next = { ...value, origen };
            onChange(next);
            onApply(next);
          }}
          className={`${INPUT_CLS} mt-1`}
        >
          <option value="">Todos</option>
          <option value="cargue">Cargue</option>
          <option value="autogenerado">Autogenerado</option>
        </select>
      </label>
      <label className="block text-xs font-semibold" htmlFor="doc-filter-estado">
        Estado
        <select
          id="doc-filter-estado"
          value={value.estado}
          onChange={(e) => {
            const estado = e.target.value as DocumentTypeCatalogFilters["estado"];
            const next = { ...value, estado };
            onChange(next);
            onApply(next);
          }}
          className={`${INPUT_CLS} mt-1`}
        >
          <option value="">Todos</option>
          <option value="activo">Activo</option>
          <option value="inactivo">Inactivo</option>
        </select>
      </label>
      <div className="flex items-end gap-2">
        <button
          type="submit"
          className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <Search className="h-3.5 w-3.5" /> Buscar
        </button>
        <button
          type="button"
          onClick={onClear}
          className="inline-flex items-center gap-1.5 rounded-xl border px-4 py-2 text-xs font-medium"
        >
          <X className="h-3.5 w-3.5" /> Limpiar
        </button>
      </div>
    </form>
  );
}
