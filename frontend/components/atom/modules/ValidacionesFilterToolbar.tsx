'use client';

import { useState, type ReactNode } from 'react';
import { RefreshCw, Search, SlidersHorizontal, X } from 'lucide-react';
import type {
  BiometricEstado,
  BiometricProvider,
  BiometricParte,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/**
 * Barra de filtros del submódulo "Validaciones de Identidad" (HU #10348). Presentacional: el estado de
 * los filtros vive en el contenedor (Validaciones.tsx), que delega el filtrado al backend vía query
 * params (HU #10347) — NO se filtra client-side sobre el cap de 500 filas. Compacta para no robarle alto
 * a la grilla: dropdowns en vez de chips y los filtros secundarios (documento, score, fechas) plegados
 * tras "Más filtros". WCAG 2.1 AA: cada control tiene etiqueta asociada, el toggle expone aria-expanded
 * y el contador es aria-live.
 */

/** Filtros de la UI (controlados). Los numéricos/fechas se guardan como string para el input. */
export interface ValidacionesUiFilters {
  referenceNumber: string;
  nombre: string;
  tipoDoc: string;
  documento: string;
  modalidad: '' | WizardModalidad;
  parte: '' | BiometricParte;
  estado: '' | BiometricEstado;
  provider: '' | BiometricProvider;
  scoreMin: string;
  scoreMax: string;
  createdFrom: string;
  createdTo: string;
  motivoRechazo: string;
}

export const EMPTY_VALIDACIONES_FILTERS: ValidacionesUiFilters = {
  referenceNumber: '',
  nombre: '',
  tipoDoc: '',
  documento: '',
  modalidad: '',
  parte: '',
  estado: '',
  provider: '',
  scoreMin: '',
  scoreMax: '',
  createdFrom: '',
  createdTo: '',
  motivoRechazo: '',
};

/** True si hay al menos un criterio de filtrado informado. */
export function hasActiveValidacionesFilters(f: ValidacionesUiFilters): boolean {
  return (
    f.referenceNumber.trim() !== '' ||
    f.nombre.trim() !== '' ||
    f.tipoDoc.trim() !== '' ||
    f.documento.trim() !== '' ||
    f.modalidad !== '' ||
    f.parte !== '' ||
    f.estado !== '' ||
    f.provider !== '' ||
    f.scoreMin.trim() !== '' ||
    f.scoreMax.trim() !== '' ||
    f.createdFrom !== '' ||
    f.createdTo !== '' ||
    f.motivoRechazo.trim() !== ''
  );
}

/** True si hay algún filtro AVANZADO informado (para abrir el panel automáticamente). */
function hasActiveAdvanced(f: ValidacionesUiFilters): boolean {
  return (
    f.tipoDoc.trim() !== '' ||
    f.documento.trim() !== '' ||
    f.scoreMin.trim() !== '' ||
    f.scoreMax.trim() !== '' ||
    f.createdFrom !== '' ||
    f.createdTo !== ''
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
}

const MODALIDAD_OPTIONS: { value: '' | WizardModalidad; label: string }[] = [
  { value: '', label: 'Todas' },
  { value: 'matricula_inicial', label: 'Matrícula inicial' },
  { value: 'traspaso', label: 'Traspaso' },
];

const PARTE_OPTIONS: { value: '' | BiometricParte; label: string }[] = [
  { value: '', label: 'Todas' },
  { value: 'comprador', label: 'Comprador' },
  { value: 'vendedor', label: 'Vendedor' },
];

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

const PROVIDER_OPTIONS: { value: '' | BiometricProvider; label: string }[] = [
  { value: '', label: 'Todos' },
  { value: 'mock', label: 'Simulado' },
  { value: 'kyverum', label: 'Kyverum' },
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
        style={{ borderColor: '#DFE5ED' }}
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
}: Props) {
  const hasActiveFilters = hasActiveValidacionesFilters(filters);
  // Panel avanzado plegado por defecto para dejarle alto a la grilla; se abre si ya hay filtros avanzados.
  const [showAdvanced, setShowAdvanced] = useState(() => hasActiveAdvanced(filters));

  const counterLabel =
    resultCount === 0
      ? 'Sin resultados'
      : `${resultCount} validación${resultCount === 1 ? '' : 'es'}`;

  return (
    <div className="rounded-2xl border bg-white p-3 dark:bg-[#0B0F14] shrink-0" style={{ borderColor: '#DFE5ED' }}>
      {/* Trámite (búsqueda) + Actualizar */}
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <Search
            className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-40"
            aria-hidden="true"
          />
          <input
            type="search"
            value={filters.referenceNumber}
            onChange={(e) => onChange({ referenceNumber: e.target.value })}
            placeholder="Buscar por número de trámite…"
            aria-label="Filtrar por número de trámite"
            className={`${CONTROL_CLASS} pl-9`}
            style={{ borderColor: '#DFE5ED' }}
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

      {/* Filtros principales: dropdowns + persona (siempre visibles) */}
      <div className="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
        <FilterSelect
          label="Modalidad"
          value={filters.modalidad}
          options={MODALIDAD_OPTIONS}
          onSelect={(v) => onChange({ modalidad: v }, true)}
        />
        <FilterSelect
          label="Parte"
          value={filters.parte}
          options={PARTE_OPTIONS}
          onSelect={(v) => onChange({ parte: v }, true)}
        />
        <FilterSelect
          label="Estado"
          value={filters.estado}
          options={ESTADO_OPTIONS}
          onSelect={(v) => onChange({ estado: v }, true)}
        />
        <FilterSelect
          label="Proveedor"
          value={filters.provider}
          options={PROVIDER_OPTIONS}
          onSelect={(v) => onChange({ provider: v }, true)}
        />
        <Field label="Persona">
          <input
            type="text"
            value={filters.nombre}
            onChange={(e) => onChange({ nombre: e.target.value })}
            placeholder="Nombre…"
            className={CONTROL_CLASS}
            style={{ borderColor: '#DFE5ED' }}
          />
        </Field>
      </div>

      {/* Motivo de rechazo: contextual (visible solo cuando se filtra por estado=rechazado) */}
      {filters.estado === 'rechazado' && (
        <div className="mt-2">
          <Field label="Motivo de rechazo">
            <input
              type="text"
              value={filters.motivoRechazo}
              onChange={(e) => onChange({ motivoRechazo: e.target.value })}
              placeholder="Texto del motivo…"
              className={CONTROL_CLASS}
              style={{ borderColor: '#DFE5ED' }}
            />
          </Field>
        </div>
      )}

      {/* Filtros avanzados (plegables): documento, score, fechas */}
      {showAdvanced && (
        <div id="validaciones-filtros-avanzados" className="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-6">
          <Field label="Tipo doc.">
            <input
              type="text"
              value={filters.tipoDoc}
              onChange={(e) => onChange({ tipoDoc: e.target.value })}
              placeholder="CC, CE…"
              className={CONTROL_CLASS}
              style={{ borderColor: '#DFE5ED' }}
            />
          </Field>
          <Field label="Documento">
            <input
              type="text"
              value={filters.documento}
              onChange={(e) => onChange({ documento: e.target.value })}
              placeholder="Número…"
              className={CONTROL_CLASS}
              style={{ borderColor: '#DFE5ED' }}
            />
          </Field>
          <Field label="Score mín.">
            <input
              type="number"
              min={0}
              max={100}
              value={filters.scoreMin}
              onChange={(e) => onChange({ scoreMin: e.target.value })}
              className={CONTROL_CLASS}
              style={{ borderColor: '#DFE5ED' }}
            />
          </Field>
          <Field label="Score máx.">
            <input
              type="number"
              min={0}
              max={100}
              value={filters.scoreMax}
              onChange={(e) => onChange({ scoreMax: e.target.value })}
              className={CONTROL_CLASS}
              style={{ borderColor: '#DFE5ED' }}
            />
          </Field>
          <Field label="Desde">
            <input
              type="date"
              value={filters.createdFrom}
              onChange={(e) => onChange({ createdFrom: e.target.value }, true)}
              className={CONTROL_CLASS}
              style={{ borderColor: '#DFE5ED' }}
            />
          </Field>
          <Field label="Hasta">
            <input
              type="date"
              value={filters.createdTo}
              onChange={(e) => onChange({ createdTo: e.target.value }, true)}
              className={CONTROL_CLASS}
              style={{ borderColor: '#DFE5ED' }}
            />
          </Field>
        </div>
      )}

      {/* Contador + más filtros + limpiar */}
      <div className="mt-2 flex items-center justify-between gap-3 border-t pt-2" style={{ borderColor: '#DFE5ED' }}>
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
