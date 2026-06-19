"use client";

import { useState } from "react";
import { Search, X } from "lucide-react";

// Panel de filtros del listado de compañías (HU #10194, AC1). El filtrado es
// server-side: NIT/Razón Social/fechas se confirman al pulsar "Buscar"; el
// Estado se aplica de inmediato al seleccionarlo. "Limpiar" resetea todo.
export interface CompanyFilters {
  nit?: string;
  razonSocial?: string;
  estadoActivo?: boolean;
  fechaDesde?: string;
  fechaHasta?: string;
}

export interface CompanyFiltersPanelProps {
  /** Se invoca al confirmar los filtros (botón Buscar). */
  onApply: (filters: CompanyFilters) => void;
  initialValue?: CompanyFilters;
}

const EMPTY: CompanyFilters = {};

const INPUT_CLS =
  "w-full rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF] border-[#DFE5ED]";

export function CompanyFiltersPanel({ onApply, initialValue = EMPTY }: CompanyFiltersPanelProps) {
  const [filters, setFilters] = useState<CompanyFilters>(initialValue);

  const estadoValue =
    filters.estadoActivo === undefined ? "" : filters.estadoActivo ? "true" : "false";

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onApply(normalize(filters));
  };

  const handleClear = () => {
    setFilters(EMPTY);
    onApply(EMPTY);
  };

  return (
    <form
      onSubmit={handleSubmit}
      aria-label="Filtros de compañías"
      className="grid grid-cols-1 gap-3 rounded-2xl border bg-white p-4 md:grid-cols-6 dark:bg-[#0B0F14]"
      style={{ borderColor: "#DFE5ED" }}
    >
      <Field label="NIT" htmlFor="filter-nit">
        <input
          id="filter-nit"
          value={filters.nit ?? ""}
          onChange={(e) => setFilters((f) => ({ ...f, nit: e.target.value }))}
          className={INPUT_CLS}
          placeholder="900.123.456-7"
        />
      </Field>

      <Field label="Razón Social" htmlFor="filter-razon" className="md:col-span-2">
        <input
          id="filter-razon"
          value={filters.razonSocial ?? ""}
          onChange={(e) => setFilters((f) => ({ ...f, razonSocial: e.target.value }))}
          className={INPUT_CLS}
          placeholder="Nombre de la compañía"
        />
      </Field>

      <Field label="Estado" htmlFor="filter-estado">
        <select
          id="filter-estado"
          value={estadoValue}
          onChange={(e) => {
            const estadoActivo =
              e.target.value === "" ? undefined : e.target.value === "true";
            const next = { ...filters, estadoActivo };
            setFilters(next);
            // El Estado aplica de inmediato (sin pulsar Buscar), conservando los
            // demás filtros ya escritos.
            onApply(normalize(next));
          }}
          className={INPUT_CLS}
        >
          <option value="">Todos</option>
          <option value="true">Activa</option>
          <option value="false">Inactiva</option>
        </select>
      </Field>

      <Field label="Fecha desde" htmlFor="filter-desde">
        <input
          id="filter-desde"
          type="date"
          value={filters.fechaDesde ?? ""}
          onChange={(e) => setFilters((f) => ({ ...f, fechaDesde: e.target.value }))}
          className={INPUT_CLS}
        />
      </Field>

      <Field label="Fecha hasta" htmlFor="filter-hasta">
        <input
          id="filter-hasta"
          type="date"
          value={filters.fechaHasta ?? ""}
          onChange={(e) => setFilters((f) => ({ ...f, fechaHasta: e.target.value }))}
          className={INPUT_CLS}
        />
      </Field>

      <div className="flex items-end gap-2 md:col-span-6">
        <button
          type="submit"
          className="flex items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold text-white"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <Search className="h-4 w-4" /> Buscar
        </button>
        <button
          type="button"
          onClick={handleClear}
          className="flex items-center gap-2 rounded-xl border px-4 py-2.5 text-sm font-medium"
          style={{ borderColor: "#DFE5ED" }}
        >
          <X className="h-4 w-4" /> Limpiar
        </button>
      </div>

    </form>
  );
}

function Field({
  label,
  htmlFor,
  className,
  children,
}: {
  label: string;
  htmlFor: string;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <div className={className}>
      <label htmlFor={htmlFor} className="mb-1 block text-[11px] font-semibold opacity-70">
        {label}
      </label>
      {children}
    </div>
  );
}

function normalize(filters: CompanyFilters): CompanyFilters {
  const trimmed: CompanyFilters = {
    nit: filters.nit?.trim() || undefined,
    razonSocial: filters.razonSocial?.trim() || undefined,
    estadoActivo: filters.estadoActivo,
    fechaDesde: filters.fechaDesde || undefined,
    fechaHasta: filters.fechaHasta || undefined,
  };
  return trimmed;
}
