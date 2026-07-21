"use client";

import type { CompanyListItem } from "@/lib/api/types";
import { CompanySelector } from "../_reportes/CompanySelector";
import { DateRangeFilter } from "../_reportes/DateRangeFilter";
import type { DetailedReportFiltersState, TriState } from "./filters";

const inputClass =
  "h-10 rounded-[10px] border bg-white px-3 text-xs font-medium text-[#162744] outline-none focus:border-[#557EFF] dark:bg-[#0B0F14] dark:text-white";

interface DetailedReportFiltersPanelProps {
  filters: DetailedReportFiltersState;
  onChange: (next: DetailedReportFiltersState) => void;
  onSearch: () => void;
  isSuper: boolean;
  companies: CompanyListItem[];
  compact?: boolean;
}

export function DetailedReportFiltersPanel({
  filters,
  onChange,
  onSearch,
  isSuper,
  companies,
  compact = false,
}: DetailedReportFiltersPanelProps) {
  function patch(partial: Partial<DetailedReportFiltersState>) {
    onChange({ ...filters, ...partial });
  }

  return (
    <div className={`flex flex-col gap-3 ${compact ? "" : "rounded-2xl border p-4 bg-white dark:bg-[#0B0F14]"}`}>
      <div className="flex flex-wrap items-end gap-3">
        <DateRangeFilter value={filters.range} onChange={(range) => patch({ range })} />
        {isSuper && (
          <CompanySelector
            companies={companies}
            value={filters.tenantId}
            onChange={(tenantId) => patch({ tenantId })}
          />
        )}
        <FilterInput id="dr-ref" label="N.º OT / radicado" value={filters.referenceNumber ?? ""} onChange={(referenceNumber) => patch({ referenceNumber })} />
        <FilterInput id="dr-cat" label="Categoría" value={filters.category ?? ""} onChange={(category) => patch({ category })} placeholder="matriculas|traspasos|otros" />
        <FilterInput id="dr-ptype" label="Tipo trámite (ID)" value={filters.procedureTypeId ?? ""} onChange={(procedureTypeId) => patch({ procedureTypeId })} />
        <FilterInput id="dr-ot" label="Organismo (ID)" value={filters.transitOfficeId ?? ""} onChange={(transitOfficeId) => patch({ transitOfficeId })} />
        <FilterInput id="dr-status" label="Estado" value={filters.status ?? ""} onChange={(status) => patch({ status })} />
        <FilterInput id="dr-doc" label="Documento persona" value={filters.personDocument ?? ""} onChange={(personDocument) => patch({ personDocument })} />
        <FilterInput id="dr-name" label="Nombre persona" value={filters.personName ?? ""} onChange={(personName) => patch({ personName })} />
        <TriSelect id="dr-transform" label="Transformación" value={filters.hasTransformation} onChange={(hasTransformation) => patch({ hasTransformation })} />
        <TriSelect id="dr-leasing" label="Leasing" value={filters.isLeasing} onChange={(isLeasing) => patch({ isLeasing })} />
        <button
          type="button"
          onClick={onSearch}
          className="h-10 rounded-[10px] px-4 text-xs font-semibold text-white"
          style={{ background: "#557EFF" }}
        >
          Buscar
        </button>
      </div>
    </div>
  );
}

function FilterInput({
  id,
  label,
  value,
  onChange,
  placeholder,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  return (
    <div className="flex flex-col gap-1 min-w-[140px]">
      <label htmlFor={id} className="text-[10px] font-semibold uppercase opacity-60">{label}</label>
      <input id={id} className={inputClass} value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
    </div>
  );
}

function TriSelect({
  id,
  label,
  value,
  onChange,
}: {
  id: string;
  label: string;
  value: TriState;
  onChange: (value: TriState) => void;
}) {
  return (
    <div className="flex flex-col gap-1 min-w-[120px]">
      <label htmlFor={id} className="text-[10px] font-semibold uppercase opacity-60">{label}</label>
      <select id={id} className={inputClass} value={value} onChange={(e) => onChange(e.target.value as TriState)}>
        <option value="">Todos</option>
        <option value="true">Sí</option>
        <option value="false">No</option>
      </select>
    </div>
  );
}
