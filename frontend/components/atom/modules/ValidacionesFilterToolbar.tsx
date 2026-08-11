'use client';

import { useState, type ReactNode } from 'react';
import { RefreshCw, Search, SlidersHorizontal, X } from 'lucide-react';
import type {
  BiometricEstado,
  BiometricVigenciaEstado,
} from '@/lib/api/types/procedure-runtime';
import { digitsOnly } from '@/lib/format/currency';
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from '@/lib/validation/fieldRules';

/**
 * Barra de filtros del módulo de Identidad (HU #10348). Presentacional: el estado de los filtros vive
 * en el contenedor (Validaciones.tsx), que delega el filtrado al backend vía query params (HU #10347).
 *
 * La grilla agrupa POR PERSONA (HU #11271 / ADR-0040), así que aquí solo hay filtros con semántica de
 * persona. Los criterios propios de UNA validación —número de trámite, modalidad, parte, proveedor,
 * score, motivo de rechazo— se retiraron: no describen a la persona y viven en su detalle.
 *
 * Compacta para no robarle alto a la grilla: los filtros secundarios (fechas de registro y vigencia)
 * van plegados tras "Más filtros". WCAG 2.1 AA: cada control tiene etiqueta asociada, el toggle expone
 * aria-expanded y el contador es aria-live.
 */

/** Filtros de la UI (controlados). Los numéricos/fechas se guardan como string para el input. */
export interface ValidacionesUiFilters {
  name: string;
  documentNumber: string;
  status: '' | BiometricEstado;
  vigenciaEstado: '' | BiometricVigenciaEstado;
  createdFrom: string;
  createdTo: string;
  expiraDesde: string;
  expiraHasta: string;
  /** "Vence en ≤ N días" (string para el input numérico; vacío = sin filtro). */
  venceEnDias: string;
}

export const EMPTY_VALIDACIONES_FILTERS: ValidacionesUiFilters = {
  name: '',
  documentNumber: '',
  status: '',
  vigenciaEstado: '',
  createdFrom: '',
  createdTo: '',
  expiraDesde: '',
  expiraHasta: '',
  venceEnDias: '',
};

/** True si hay al menos un criterio de filtrado informado. */
export function hasActiveValidacionesFilters(f: ValidacionesUiFilters): boolean {
  return (
    f.name.trim() !== '' ||
    f.documentNumber.trim() !== '' ||
    f.status !== '' ||
    f.vigenciaEstado !== '' ||
    f.createdFrom !== '' ||
    f.createdTo !== '' ||
    f.expiraDesde !== '' ||
    f.expiraHasta !== '' ||
    f.venceEnDias.trim() !== ''
  );
}

/** True si hay algún filtro AVANZADO informado (para abrir el panel automáticamente). */
function hasActiveAdvanced(f: ValidacionesUiFilters): boolean {
  return (
    f.createdFrom !== '' ||
    f.createdTo !== '' ||
    f.expiraDesde !== '' ||
    f.expiraHasta !== '' ||
    f.venceEnDias.trim() !== ''
  );
}

interface Props {
  filters: ValidacionesUiFilters;
  /** Aplica un parche de filtros. `immediate` (selects/fechas) refetch inmediato; ausente (texto) debounced. */
  onChange: (patch: Partial<ValidacionesUiFilters>, immediate?: boolean) => void;
  onRefresh: () => void;
  onClearFilters: () => void;
  loading?: boolean;
  resultCount: number;
  /** Etiqueta del contador (p.ej. "12 personas"). */
  resultCountLabel?: string;
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
  { value: 'por_vencer', label: 'Por vencer (≤7 días)' },
  { value: 'vencida', label: 'Vencida' },
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

/** Dropdown de filtro (reemplaza a los chips para ahorrar espacio). Incluye opción "Todos/Todas". */
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
      <select
        value={value}
        onChange={(e) => onSelect(e.target.value as T)}
        className={CONTROL_CLASS}
      >
        {options.map((o) => (
          <option key={o.value || 'todos'} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </Field>
  );
}

export function ValidacionesFilterToolbar({
  filters,
  onChange,
  onRefresh,
  onClearFilters,
  loading = false,
  resultCount,
  resultCountLabel,
}: Props) {
  const hasActiveFilters = hasActiveValidacionesFilters(filters);
  // Panel avanzado plegado por defecto para dejarle alto a la grilla; se abre si ya hay filtros avanzados.
  const [showAdvanced, setShowAdvanced] = useState(() => hasActiveAdvanced(filters));

  const counterLabel =
    resultCountLabel ??
    (resultCount === 0
      ? 'Sin resultados'
      : `${resultCount} persona${resultCount === 1 ? '' : 's'}`);

  return (
    <div className="rounded-2xl border bg-white p-3 dark:bg-[#0B0F14] shrink-0">
      {/* Persona (búsqueda) + Actualizar */}
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <Search
            className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-40"
            aria-hidden="true"
          />
          <input
            type="search"
            value={filters.name}
            onChange={(e) => onChange({ name: sanitizeNoAngleBrackets(e.target.value) })}
            maxLength={SEARCH_TEXT_MAX_LENGTH}
            placeholder="Buscar por nombre de la persona…"
            aria-label="Filtrar por nombre de la persona"
            className={`${CONTROL_CLASS} pl-9`}
          />
        </div>
        <button
          type="button"
          onClick={onRefresh}
          disabled={loading}
          className="flex shrink-0 items-center justify-center gap-1.5 rounded-xl border px-4 py-2 text-[11px] font-semibold disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
          aria-label="Actualizar validaciones de identidad"
        >
          <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} aria-hidden="true" />
          Actualizar
        </button>
      </div>

      {/* Filtros principales: estado, vigencia y número de documento (siempre visibles). El tipo de
          documento no filtra: el número ya identifica a la persona dentro del tenant. */}
      <div className="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-3">
        <FilterSelect
          label="Estado"
          value={filters.status}
          options={ESTADO_OPTIONS}
          onSelect={(v) => onChange({ status: v }, true)}
        />
        <FilterSelect
          label="Vigencia"
          value={filters.vigenciaEstado}
          options={VIGENCIA_OPTIONS}
          onSelect={(v) => onChange({ vigenciaEstado: v }, true)}
        />
        <Field label="Documento">
          <input
            type="text"
            inputMode="numeric"
            value={filters.documentNumber}
            onChange={(e) => onChange({ documentNumber: digitsOnly(e.target.value) })}
            maxLength={SEARCH_TEXT_MAX_LENGTH}
            placeholder="Número…"
            className={CONTROL_CLASS}
          />
        </Field>
      </div>

      {/* Filtros avanzados (plegables): fechas de registro y de vigencia */}
      {showAdvanced && (
        <div id="validaciones-filtros-avanzados" className="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
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
          <Field label="Vence desde">
            <input
              type="date"
              value={filters.expiraDesde}
              onChange={(e) => onChange({ expiraDesde: e.target.value }, true)}
              className={CONTROL_CLASS}
            />
          </Field>
          <Field label="Vence hasta">
            <input
              type="date"
              value={filters.expiraHasta}
              onChange={(e) => onChange({ expiraHasta: e.target.value }, true)}
              className={CONTROL_CLASS}
            />
          </Field>
          <Field label="Vence en ≤ N días">
            <input
              type="text"
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete="off"
              value={filters.venceEnDias}
              onChange={(e) => onChange({ venceEnDias: digitsOnly(e.target.value) })}
              placeholder="Ej. 3"
              className={CONTROL_CLASS}
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
            aria-controls="validaciones-filtros-avanzados"
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
