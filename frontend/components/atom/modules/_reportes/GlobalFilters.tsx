"use client";

// Filtros globales persistentes de Reportes 2.0 (HU-C): elevados sobre las pestañas,
// se conservan al cambiar de pestaña. Rango de fechas + compañía (SuperAdmin) +
// comparación de periodos + filtros adicionales ocultables (OT/tipo/operador por ID,
// sin catálogo disponible todavía).
import { useState } from "react";
import { SlidersHorizontal } from "lucide-react";
import type { CompanyListItem } from "@/lib/api/types";
import { CompanySelector } from "./CompanySelector";
import { DateRangeFilter } from "./DateRangeFilter";
import type { ReportFilters } from "./filters";

export interface GlobalFiltersProps {
  filters: ReportFilters;
  onChange: (next: ReportFilters) => void;
  isSuper: boolean;
  companies: CompanyListItem[];
  /**
   * Deja solo el selector de compañía.
   *
   * «Consultas» lleva su propio periodo dentro, sobre la fecha que el usuario elige. Dejar aquí
   * arriba un rango que no cambia ni un número es exactamente la promesa falsa que obligó a partir
   * la consola del organismo en pestañas: un selector de fechas presidiendo algo que no gobierna
   * enseña a desconfiar de todos los filtros de la pantalla.
   */
  onlyCompany?: boolean;
}

const inputClass =
  "h-10 rounded-[10px] border bg-white px-3 text-xs font-medium text-[#162744] outline-none focus:border-[#557EFF] disabled:opacity-60 dark:bg-[#0B0F14] dark:text-white";

export function GlobalFilters({
  filters,
  onChange,
  isSuper,
  companies,
  onlyCompany = false,
}: GlobalFiltersProps) {
  const [showAdvanced, setShowAdvanced] = useState(false);

  function patch(partial: Partial<ReportFilters>) {
    onChange({ ...filters, ...partial });
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-end gap-3">
        {!onlyCompany && (
          <DateRangeFilter value={filters.range} onChange={(range) => patch({ range })} />
        )}

        {isSuper && (
          <CompanySelector
            companies={companies}
            value={filters.tenantId}
            onChange={(tenantId) => patch({ tenantId })}
            defaultLabel="Todas las compañías"
          />
        )}

        {!onlyCompany && (
        <div className="flex flex-col gap-1">
          <label htmlFor="reportes-comparar" className="text-[10px] font-semibold uppercase opacity-60">
            Comparar con
          </label>
          <select
            id="reportes-comparar"
            className={inputClass}
            value={filters.compareWith}
            onChange={(e) => patch({ compareWith: e.target.value as ReportFilters["compareWith"] })}
            title="Compara las métricas del rango con otro periodo para ver la variación"
          >
            <option value="">Sin comparar</option>
            <option value="previous_period">Periodo anterior</option>
            <option value="previous_year">Año anterior</option>
          </select>
        </div>
        )}

        {!onlyCompany && (
        <button
          type="button"
          onClick={() => setShowAdvanced((v) => !v)}
          aria-expanded={showAdvanced}
          className="h-10 flex items-center gap-1.5 rounded-[10px] border px-3 text-xs font-semibold hover:bg-[#557EFF14]"
        >
          <SlidersHorizontal className="h-3.5 w-3.5" aria-hidden="true" />
          {showAdvanced ? "Ocultar filtros" : "Más filtros"}
        </button>
        )}
      </div>

      {showAdvanced && !onlyCompany && (
        <div className="flex flex-wrap items-end gap-3" data-testid="reportes-filtros-avanzados">
          <AdvancedInput
            id="reportes-filtro-ot"
            label="Organismo de tránsito (ID)"
            value={filters.transitOfficeId}
            onChange={(transitOfficeId) => patch({ transitOfficeId })}
          />
          <AdvancedInput
            id="reportes-filtro-tipo"
            label="Tipo de trámite (ID)"
            value={filters.procedureTypeId}
            onChange={(procedureTypeId) => patch({ procedureTypeId })}
          />
          <AdvancedInput
            id="reportes-filtro-operador"
            label="Operador (ID)"
            value={filters.operatorUserId}
            onChange={(operatorUserId) => patch({ operatorUserId })}
          />
        </div>
      )}
    </div>
  );
}

function AdvancedInput({
  id,
  label,
  value,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-[10px] font-semibold uppercase opacity-60">
        {label}
      </label>
      <input
        id={id}
        type="text"
        className={inputClass}
        value={value}
        placeholder="Identificador (opcional)"
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  );
}
