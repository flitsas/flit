"use client";

import type { ReactNode } from "react";
import type { ProcedureFamily, ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";
import type { TransitOfficeOption } from "@/lib/api/types/procedure-runtime";
import type { CompanyListItem } from "@/lib/api/types";
import { statusLabel } from "../_reportes/categories";
import { CompanySelector } from "../_reportes/CompanySelector";
import { DateRangeFilter } from "../_reportes/DateRangeFilter";
import {
  TipoTramiteSelect,
  agruparTiposTramite,
  type SeleccionTipoTramite,
} from "@/components/consultas/tipo-tramite";
import { familyToCategory, PROCEDURE_STATUSES, type DetailedReportFiltersState, type TriState } from "./filters";

/** Vuelta atrás de `familyToCategory`, para leer el filtro que ya venía guardado o en la URL. */
const CATEGORIA_A_FAMILIA: Record<string, ProcedureFamily> = {
  matriculas: "MATRICULAS",
  traspasos: "TRASPASO",
  otros: "OTROS",
};

const inputClass =
  "h-10 rounded-[10px] border bg-white px-3 text-xs font-medium text-[#162744] outline-none focus:border-[#557EFF] dark:bg-[#0B0F14] dark:text-white";

interface DetailedReportFiltersPanelProps {
  filters: DetailedReportFiltersState;
  onChange: (next: DetailedReportFiltersState) => void;
  onSearch: () => void;
  isSuper: boolean;
  companies: CompanyListItem[];
  procedureTypes: ProcedureTypeSummary[];
  transitOffices: TransitOfficeOption[];
  compact?: boolean;
}

export function DetailedReportFiltersPanel({
  filters,
  onChange,
  onSearch,
  isSuper,
  companies,
  procedureTypes,
  transitOffices,
  compact = false,
}: DetailedReportFiltersPanelProps) {
  function patch(partial: Partial<DetailedReportFiltersState>) {
    onChange({ ...filters, ...partial });
  }

  // El selector es el mismo que usan los reportes del organismo y los de la empresa: una sola
  // implementación es lo que evita que un informe ofrezca «Blindaje» y el de al lado no.
  //
  // La traducción vive aquí porque este informe filtra contra `category` de la vista BI
  // (minúsculas, en plural) y no contra el código de familia. Son el mismo eje con dos nombres, y
  // el sitio para reconciliarlos es el borde de este panel, no el componente compartido.
  const grupos = agruparTiposTramite(procedureTypes);
  const seleccion: SeleccionTipoTramite = filters.procedureTypeId
    ? { tipoId: filters.procedureTypeId }
    : filters.category
      ? { familia: CATEGORIA_A_FAMILIA[filters.category] }
      : {};

  function handleProcedureChange(sel: SeleccionTipoTramite) {
    if (sel.tipoId) patch({ procedureTypeId: sel.tipoId, category: undefined });
    else if (sel.familia) patch({ category: familyToCategory(sel.familia), procedureTypeId: undefined });
    else patch({ category: undefined, procedureTypeId: undefined });
  }

  // Único requisito para consultar: SuperAdmin debe elegir una compañía (el backend exige
  // tenantId). Para el resto de usuarios el rango de fechas basta y trae todos sus trámites.
  const canSearch = !isSuper || Boolean(filters.tenantId);

  return (
    <div className={`flex flex-col gap-3 ${compact ? "" : "rounded-2xl border p-4 bg-white dark:bg-[#0B0F14]"}`}>
      <div className="flex flex-wrap items-end gap-3">
        <DateRangeFilter value={filters.range} onChange={(range) => patch({ range })} />
        {isSuper && (
          <CompanySelector
            companies={companies}
            value={filters.tenantId ?? ""}
            onChange={(tenantId) => patch({ tenantId })}
          />
        )}

        <FilterField id="dr-type" label="Tipo de trámite" minWidth={200}>
          <TipoTramiteSelect
            id="dr-type"
            className={inputClass}
            grupos={grupos}
            value={seleccion}
            onChange={handleProcedureChange}
          />
        </FilterField>

        <FilterField id="dr-ot" label="Organismo de tránsito" minWidth={200}>
          <select
            id="dr-ot"
            className={inputClass}
            value={filters.transitOfficeId ?? ""}
            onChange={(e) => patch({ transitOfficeId: e.target.value || undefined })}
            disabled={transitOffices.length === 0}
          >
            <option value="">Todos los organismos</option>
            {transitOffices.map((office) => (
              <option key={office.id} value={office.id}>
                {office.code} — {office.name}
              </option>
            ))}
          </select>
        </FilterField>

        <FilterInput id="dr-ref" label="N.º OT / radicado" value={filters.referenceNumber ?? ""} onChange={(referenceNumber) => patch({ referenceNumber })} />

        <FilterField id="dr-status" label="Estado">
          <select
            id="dr-status"
            className={inputClass}
            value={filters.status ?? ""}
            onChange={(e) => patch({ status: e.target.value || undefined })}
          >
            <option value="">Todos los estados</option>
            {PROCEDURE_STATUSES.map((status) => (
              <option key={status} value={status}>
                {statusLabel(status)}
              </option>
            ))}
          </select>
        </FilterField>
        <FilterInput id="dr-doc" label="Documento persona" value={filters.personDocument ?? ""} onChange={(personDocument) => patch({ personDocument })} />
        <FilterInput id="dr-name" label="Nombre persona" value={filters.personName ?? ""} onChange={(personName) => patch({ personName })} />
        <TriSelect id="dr-transform" label="Transformación" value={filters.hasTransformation} onChange={(hasTransformation) => patch({ hasTransformation })} />
        <TriSelect id="dr-leasing" label="Leasing" value={filters.isLeasing} onChange={(isLeasing) => patch({ isLeasing })} />
        <button
          type="button"
          onClick={onSearch}
          disabled={!canSearch}
          className="h-10 rounded-[10px] px-4 text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: "#557EFF" }}
        >
          Buscar
        </button>
      </div>
      {!canSearch && (
        <p className="text-[11px] opacity-70">
          Selecciona una <strong>compañía</strong> para ver sus trámites. Los demás filtros son
          opcionales; con solo el rango de fechas se muestran todos los registros.
        </p>
      )}
    </div>
  );
}

function FilterField({
  id,
  label,
  minWidth = 140,
  children,
}: {
  id: string;
  label: string;
  minWidth?: number;
  children: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-1" style={{ minWidth }}>
      <label htmlFor={id} className="text-[10px] font-semibold uppercase opacity-60">{label}</label>
      {children}
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
    <FilterField id={id} label={label}>
      <input id={id} className={inputClass} value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
    </FilterField>
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
    <FilterField id={id} label={label} minWidth={120}>
      <select id={id} className={inputClass} value={value} onChange={(e) => onChange(e.target.value as TriState)}>
        <option value="">Todos</option>
        <option value="true">Sí</option>
        <option value="false">No</option>
      </select>
    </FilterField>
  );
}
