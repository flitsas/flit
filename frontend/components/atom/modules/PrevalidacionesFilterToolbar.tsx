'use client';

import { useState, type ReactNode } from 'react';
import { Search, SlidersHorizontal, X } from 'lucide-react';
import type { BiometricEstado, BiometricVigenciaEstado } from '@/lib/api/types/procedure-runtime';
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from '@/lib/validation/fieldRules';

/**
 * Barra de filtros del módulo Prevalidaciones (standalone). Presentacional: el estado vive en
 * `PrevalidacionesModule`. Fila principal alineada (persona + documento + estado); avanzados
 * plegables. Sin botón Actualizar: el listado se refresca en vivo / al cambiar filtros.
 */

export interface PrevalidacionesUiFilters {
  name: string;
  documentType: string;
  documentNumber: string;
  status: '' | BiometricEstado;
  vigenciaEstado: '' | BiometricVigenciaEstado;
  createdFrom: string;
  createdTo: string;
  rejectionReason: string;
}

export const EMPTY_PREVALIDACIONES_FILTERS: PrevalidacionesUiFilters = {
  name: '',
  documentType: '',
  documentNumber: '',
  status: '',
  vigenciaEstado: '',
  createdFrom: '',
  createdTo: '',
  rejectionReason: '',
};

export function hasActivePrevalidacionesFilters(f: PrevalidacionesUiFilters): boolean {
  return (
    f.name.trim() !== '' ||
    f.documentType.trim() !== '' ||
    f.documentNumber.trim() !== '' ||
    f.status !== '' ||
    f.vigenciaEstado !== '' ||
    f.createdFrom !== '' ||
    f.createdTo !== '' ||
    f.rejectionReason.trim() !== ''
  );
}

function hasActiveAdvanced(f: PrevalidacionesUiFilters): boolean {
  return (
    f.documentType.trim() !== '' ||
    f.createdFrom !== '' ||
    f.createdTo !== '' ||
    f.vigenciaEstado !== ''
  );
}

const ESTADO_OPTIONS: { value: '' | BiometricEstado; label: string }[] = [
  { value: '', label: 'Todos' },
  { value: 'enviado', label: 'Enviado' },
  { value: 'en_proceso', label: 'En proceso' },
  { value: 'aprobado', label: 'Aprobado' },
  { value: 'rechazado', label: 'Rechazado' },
  { value: 'expirado', label: 'Expirado' },
  { value: 'pendiente_envio', label: 'Pendiente de envío' },
  { value: 'error_envio', label: 'Error de envío' },
];

const VIGENCIA_OPTIONS: { value: '' | BiometricVigenciaEstado; label: string }[] = [
  { value: '', label: 'Todas' },
  { value: 'vigente', label: 'Vigente' },
  { value: 'por_vencer', label: 'Por vencer' },
  { value: 'vencida', label: 'Vencida' },
];

const CONTROL_CLASS =
  'h-9 w-full rounded-lg border bg-white px-2.5 text-xs dark:bg-[#0B0F14] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]';

interface Props {
  filters: PrevalidacionesUiFilters;
  onChange: (patch: Partial<PrevalidacionesUiFilters>, immediate?: boolean) => void;
  onClearFilters: () => void;
  resultCount: number;
}

export function PrevalidacionesFilterToolbar({
  filters,
  onChange,
  onClearFilters,
  resultCount,
}: Props) {
  const [showAdvanced, setShowAdvanced] = useState(() => hasActiveAdvanced(filters));
  const hasActiveFilters = hasActivePrevalidacionesFilters(filters);
  const counterLabel =
    resultCount === 1 ? '1 prevalidación' : `${resultCount} prevalidaciones`;

  return (
    <section
      className="shrink-0 rounded-2xl border bg-white p-3 dark:bg-[#0B0F14]"
      aria-label="Filtros de prevalidaciones"
    >
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_minmax(0,0.9fr)]">
        <Field label="Persona">
          <div className="relative">
            <Search
              className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-45"
              aria-hidden
            />
            <input
              id="prevalidaciones-filtro-persona"
              type="search"
              value={filters.name}
              onChange={(e) => onChange({ name: sanitizeNoAngleBrackets(e.target.value) })}
              maxLength={SEARCH_TEXT_MAX_LENGTH}
              placeholder="Buscar por nombre…"
              className={`${CONTROL_CLASS} pl-8`}
              aria-label="Buscar por persona"
            />
          </div>
        </Field>
        <Field label="Documento">
          <input
            id="prevalidaciones-filtro-documento"
            type="search"
            value={filters.documentNumber}
            onChange={(e) => onChange({ documentNumber: sanitizeNoAngleBrackets(e.target.value) })}
            maxLength={SEARCH_TEXT_MAX_LENGTH}
            placeholder="Número de documento…"
            className={CONTROL_CLASS}
            aria-label="Buscar por documento"
          />
        </Field>
        <FilterSelect
          label="Estado"
          value={filters.status}
          options={ESTADO_OPTIONS}
          onSelect={(v) => onChange({ status: v }, true)}
        />
      </div>

      {filters.status === 'rechazado' && (
        <div className="mt-2">
          <Field label="Motivo de rechazo">
            <input
              type="text"
              value={filters.rejectionReason}
              onChange={(e) => onChange({ rejectionReason: sanitizeNoAngleBrackets(e.target.value) })}
              maxLength={SEARCH_TEXT_MAX_LENGTH}
              placeholder="Texto del motivo…"
              className={CONTROL_CLASS}
            />
          </Field>
        </div>
      )}

      {showAdvanced && (
        <div
          id="prevalidaciones-filtros-avanzados"
          className="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-4"
        >
          <Field label="Tipo doc.">
            <input
              type="text"
              value={filters.documentType}
              onChange={(e) => onChange({ documentType: sanitizeNoAngleBrackets(e.target.value) })}
              maxLength={SEARCH_TEXT_MAX_LENGTH}
              placeholder="CC, CE…"
              className={CONTROL_CLASS}
            />
          </Field>
          <FilterSelect
            label="Vigencia"
            value={filters.vigenciaEstado}
            options={VIGENCIA_OPTIONS}
            onSelect={(v) => onChange({ vigenciaEstado: v }, true)}
          />
          <Field label="Registro desde">
            <input
              type="date"
              value={filters.createdFrom}
              onChange={(e) => onChange({ createdFrom: e.target.value }, true)}
              className={CONTROL_CLASS}
            />
          </Field>
          <Field label="Registro hasta">
            <input
              type="date"
              value={filters.createdTo}
              onChange={(e) => onChange({ createdTo: e.target.value }, true)}
              className={CONTROL_CLASS}
            />
          </Field>
        </div>
      )}

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
            aria-controls="prevalidaciones-filtros-avanzados"
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
    </section>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block space-y-1">
      <span className="text-[10px] font-semibold uppercase tracking-wide opacity-55">{label}</span>
      {children}
    </label>
  );
}

function FilterSelect<T extends string>({
  label,
  value,
  options,
  onSelect,
}: {
  label: string;
  value: T;
  options: { value: T; label: string }[];
  onSelect: (value: T) => void;
}) {
  return (
    <Field label={label}>
      <select
        value={value}
        onChange={(e) => onSelect(e.target.value as T)}
        className={CONTROL_CLASS}
        aria-label={label}
      >
        {options.map((o) => (
          <option key={o.value || 'all'} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </Field>
  );
}
