"use client";

import { Icono } from "@flit/ui/Icono";
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from "@/lib/validation/fieldRules";
import { buttonClass } from "@flit/ui/Button";
import { TRAMITES_INPUT_CLS } from "@/components/operacion/tramites-control-styles";

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

// HU #14483 — el `.control` del prototipo, el mismo de la barra de Trámites.
const INPUT_CLS = `${TRAMITES_INPUT_CLS} w-full`;

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
          className={buttonClass({ variant: "primary", size: "sm" })}
        >
          <Icono nombre="buscar" className="h-3.5 w-3.5" /> Buscar
        </button>
        <button
          type="button"
          onClick={onClear}
          className={buttonClass({ variant: "secondary", size: "sm" })}
        >
          <Icono nombre="cerrar" className="h-3.5 w-3.5" /> Limpiar
        </button>
      </div>
    </form>
  );
}
