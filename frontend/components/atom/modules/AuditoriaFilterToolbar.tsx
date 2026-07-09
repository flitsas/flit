'use client';

import { useState, type ReactNode } from 'react';
import { RefreshCw, SlidersHorizontal, X } from 'lucide-react';
import type { AdminAuditModule, AdminAuditResult, AdminAuditTenantType } from '@/lib/api/types';
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from '@/lib/validation/fieldRules';

/**
 * Barra de filtros del módulo "Auditoría" (HU #10680). Presentacional: el estado de los
 * filtros vive en el contenedor (Auditoria.tsx), que delega el filtrado al backend vía
 * query params (GET /api/v1/superadmin/audit, HU #10679) — no hay filtrado client-side.
 * Calcado de ValidacionesFilterToolbar: dropdowns/fechas aplican de inmediato, texto con
 * debounce (a cargo del contenedor). WCAG 2.1 AA: cada control tiene etiqueta asociada, el
 * toggle de "Más filtros" expone aria-expanded y el contador es aria-live.
 */

/** Filtros de la UI (controlados, todos string). */
export interface AuditoriaUiFilters {
  userId: string;
  tenantId: string;
  tenantType: '' | AdminAuditTenantType;
  module: '' | AdminAuditModule;
  operation: string;
  result: '' | AdminAuditResult;
  dateFrom: string;
  dateTo: string;
}

export const EMPTY_AUDITORIA_FILTERS: AuditoriaUiFilters = {
  userId: '',
  tenantId: '',
  tenantType: '',
  module: '',
  operation: '',
  result: '',
  dateFrom: '',
  dateTo: '',
};

/** True si hay al menos un criterio de filtrado informado. */
export function hasActiveAuditoriaFilters(f: AuditoriaUiFilters): boolean {
  return (
    f.userId.trim() !== '' ||
    f.tenantId.trim() !== '' ||
    f.tenantType !== '' ||
    f.module !== '' ||
    f.operation.trim() !== '' ||
    f.result !== '' ||
    f.dateFrom !== '' ||
    f.dateTo !== ''
  );
}

/** True si hay algún filtro AVANZADO informado (para abrir el panel automáticamente). */
function hasActiveAdvanced(f: AuditoriaUiFilters): boolean {
  return f.tenantId.trim() !== '' || f.tenantType !== '' || f.dateFrom !== '' || f.dateTo !== '';
}

interface Props {
  filters: AuditoriaUiFilters;
  /** Aplica un parche de filtros. `immediate` (selects/fechas) refetch inmediato; ausente (texto) debounced. */
  onChange: (patch: Partial<AuditoriaUiFilters>, immediate?: boolean) => void;
  onRefresh: () => void;
  onClearFilters: () => void;
  loading?: boolean;
  resultCount: number;
}

const TENANT_TYPE_OPTIONS: { value: '' | AdminAuditTenantType; label: string }[] = [
  { value: '', label: 'Todos' },
  { value: 'COMPANY', label: 'Compañía gestora' },
  { value: 'TRANSIT_OFFICE', label: 'Organismo de tránsito' },
];

const MODULE_OPTIONS: { value: '' | AdminAuditModule; label: string }[] = [
  { value: '', label: 'Todos' },
  { value: 'users', label: 'Usuarios' },
  { value: 'roles', label: 'Roles' },
  { value: 'permissions', label: 'Permisos' },
  { value: 'authentication', label: 'Autenticación' },
  { value: 'security', label: 'Seguridad' },
  { value: 'config', label: 'Configuración' },
];

const RESULT_OPTIONS: { value: '' | AdminAuditResult; label: string }[] = [
  { value: '', label: 'Todos' },
  { value: 'success', label: 'Éxito' },
  { value: 'failure', label: 'Fallo' },
];

const CONTROL_CLASS =
  'w-full rounded-xl border bg-white py-2 px-2 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]';

/** Campo etiquetado (label asociado al control → nombre accesible = texto de la etiqueta). */
function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-[10px] font-semibold uppercase opacity-50">{label}</span>
      {children}
    </label>
  );
}

/** Dropdown de filtro. Incluye opción "Todos". */
function FilterSelect<T extends string>({
  label,
  value,
  options,
  onSelect,
}: {
  label: string;
  value: T;
  options: { value: T; label: string }[];
  onSelect: (v: T) => void;
}) {
  return (
    <Field label={label}>
      <select value={value} onChange={(e) => onSelect(e.target.value as T)} className={CONTROL_CLASS}>
        {options.map((o) => (
          <option key={o.value || 'todos'} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </Field>
  );
}

export function AuditoriaFilterToolbar({
  filters,
  onChange,
  onRefresh,
  onClearFilters,
  loading = false,
  resultCount,
}: Props) {
  const hasActiveFilters = hasActiveAuditoriaFilters(filters);
  // Panel avanzado plegado por defecto; se abre si ya hay filtros avanzados aplicados.
  const [showAdvanced, setShowAdvanced] = useState(() => hasActiveAdvanced(filters));

  const counterLabel =
    resultCount === 0 ? 'Sin resultados' : `${resultCount} registro${resultCount === 1 ? '' : 's'}`;

  return (
    <div className="rounded-2xl border bg-white p-3 dark:bg-[#0B0F14] shrink-0">
      {/* Usuario (actor o afectado) + Actualizar */}
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <div className="flex-1">
          <Field label="Usuario (actor o afectado)">
            <input
              type="text"
              value={filters.userId}
              onChange={(e) => onChange({ userId: sanitizeNoAngleBrackets(e.target.value) })}
              maxLength={SEARCH_TEXT_MAX_LENGTH}
              placeholder="UUID del usuario…"
              aria-label="Filtrar por usuario actor o afectado"
              className={CONTROL_CLASS}
            />
          </Field>
        </div>
        <button
          type="button"
          onClick={onRefresh}
          disabled={loading}
          className="mt-5 flex shrink-0 items-center justify-center gap-1.5 rounded-xl border px-4 py-2 text-[11px] font-semibold disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 sm:mt-0"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
          aria-label="Actualizar auditoría"
        >
          <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} aria-hidden="true" />
          Actualizar
        </button>
      </div>

      {/* Filtros principales: siempre visibles */}
      <div className="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
        <FilterSelect
          label="Módulo"
          value={filters.module}
          options={MODULE_OPTIONS}
          onSelect={(v) => onChange({ module: v }, true)}
        />
        <Field label="Operación">
          <input
            type="text"
            value={filters.operation}
            onChange={(e) => onChange({ operation: sanitizeNoAngleBrackets(e.target.value) })}
            maxLength={SEARCH_TEXT_MAX_LENGTH}
            placeholder="create, login, assign_role…"
            className={CONTROL_CLASS}
          />
        </Field>
        <FilterSelect
          label="Resultado"
          value={filters.result}
          options={RESULT_OPTIONS}
          onSelect={(v) => onChange({ result: v }, true)}
        />
      </div>

      {/* Filtros avanzados (plegables): tenant, tipo de tenant, rango de fechas */}
      {showAdvanced && (
        <div id="auditoria-filtros-avanzados" className="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
          <Field label="Compañía / OT (tenant)">
            <input
              type="text"
              value={filters.tenantId}
              onChange={(e) => onChange({ tenantId: sanitizeNoAngleBrackets(e.target.value) })}
              maxLength={SEARCH_TEXT_MAX_LENGTH}
              placeholder="UUID del tenant…"
              className={CONTROL_CLASS}
            />
          </Field>
          <FilterSelect
            label="Tipo de tenant"
            value={filters.tenantType}
            options={TENANT_TYPE_OPTIONS}
            onSelect={(v) => onChange({ tenantType: v }, true)}
          />
          <Field label="Desde">
            <input
              type="date"
              value={filters.dateFrom}
              max={filters.dateTo || undefined}
              onChange={(e) => onChange({ dateFrom: e.target.value }, true)}
              className={CONTROL_CLASS}
              aria-label="Fecha desde"
            />
          </Field>
          <Field label="Hasta">
            <input
              type="date"
              value={filters.dateTo}
              min={filters.dateFrom || undefined}
              onChange={(e) => onChange({ dateTo: e.target.value }, true)}
              className={CONTROL_CLASS}
              aria-label="Fecha hasta"
            />
          </Field>
        </div>
      )}

      {/* Contador + más filtros + limpiar */}
      <div className="mt-2 flex items-center justify-between gap-3 border-t pt-2">
        <p className="text-[11px] opacity-60" role="status" aria-live="polite">
          {counterLabel}
          {hasActiveFilters && <span className="ml-2 opacity-70">· filtros activos</span>}
        </p>
        <div className="flex shrink-0 items-center gap-3">
          <button
            type="button"
            onClick={() => setShowAdvanced((v) => !v)}
            aria-expanded={showAdvanced}
            aria-controls="auditoria-filtros-avanzados"
            className="flex items-center gap-1 text-[11px] font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ color: '#557EFF' }}
          >
            <SlidersHorizontal className="h-3 w-3" aria-hidden="true" />
            {showAdvanced ? 'Menos filtros' : 'Más filtros'}
          </button>
          {hasActiveFilters && (
            <button
              type="button"
              onClick={onClearFilters}
              className="flex items-center gap-1 text-[11px] font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ color: '#557EFF' }}
              aria-label="Limpiar filtros"
            >
              <X className="h-3 w-3" aria-hidden="true" />
              Limpiar filtros
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
