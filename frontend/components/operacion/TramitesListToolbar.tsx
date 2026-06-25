'use client';

import { RefreshCw, Search, X } from 'lucide-react';
import type {
  InstanceStatus,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/**
 * Barra de filtros del listado de trámites (Track A). Búsqueda client-side +
 * chips de modalidad/estado + Actualizar, en el estilo FLIT del módulo
 * (rounded-2xl, borde #DFE5ED, chips outline/filled). Es presentacional: el
 * estado de filtros y el filtrado viven en el contenedor (TramitesTable).
 */
interface Props {
  search: string;
  onSearchChange: (v: string) => void;
  modalidad: '' | WizardModalidad;
  onModalidadChange: (v: '' | WizardModalidad) => void;
  estado: '' | InstanceStatus;
  onEstadoChange: (v: '' | InstanceStatus) => void;
  onRefresh: () => void;
  onClearFilters: () => void;
  loading?: boolean;
  totalCount: number;
  filteredCount: number;
}

const MODALIDAD_CHIPS: { value: '' | WizardModalidad; label: string }[] = [
  { value: '', label: 'Todos' },
  { value: 'matricula_inicial', label: 'Matrícula inicial' },
  { value: 'traspaso', label: 'Traspaso' },
];

const ESTADO_CHIPS: { value: '' | InstanceStatus; label: string }[] = [
  { value: '', label: 'Todos' },
  { value: 'draft', label: 'En preparación' },
  { value: 'submitted', label: 'Enviado a tránsito' },
];

/** Chip toggle outline/filled reutilizado por ambos filtros. */
function FilterChip({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className="rounded-full border px-3 py-1.5 text-[11px] font-semibold transition"
      style={
        active
          ? { borderColor: '#557EFF', background: 'rgba(85,126,255,0.10)', color: '#557EFF' }
          : { borderColor: '#DFE5ED', color: '#162744' }
      }
    >
      {children}
    </button>
  );
}

export function TramitesListToolbar({
  search,
  onSearchChange,
  modalidad,
  onModalidadChange,
  estado,
  onEstadoChange,
  onRefresh,
  onClearFilters,
  loading = false,
  totalCount,
  filteredCount,
}: Props) {
  const hasActiveFilters =
    search.trim() !== '' || modalidad !== '' || estado !== '';

  const counterLabel =
    filteredCount === 0
      ? 'Sin resultados'
      : `${filteredCount} trámite${filteredCount === 1 ? '' : 's'}`;

  return (
    <div
      className="rounded-2xl border bg-white p-4 dark:bg-[#0B0F14]"
      style={{ borderColor: '#DFE5ED' }}
    >
      {/* Búsqueda + Actualizar */}
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <Search
            className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-40"
            aria-hidden="true"
          />
          <input
            type="search"
            value={search}
            onChange={(e) => onSearchChange(e.target.value)}
            placeholder="Buscar por placa, VIN, referencia, comprador u organismo…"
            aria-label="Buscar trámites"
            className="w-full rounded-xl border bg-white py-2 pl-9 pr-3 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]"
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>
        <button
          type="button"
          onClick={onRefresh}
          disabled={loading}
          className="flex shrink-0 items-center justify-center gap-1.5 rounded-xl border px-4 py-2 text-[11px] font-semibold disabled:opacity-50"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
          aria-label="Actualizar listado de trámites"
        >
          <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} />
          Actualizar
        </button>
      </div>

      {/* Filtros por chips */}
      <div className="mt-3 flex flex-col gap-2 lg:flex-row lg:items-center lg:gap-6">
        <div className="flex flex-wrap items-center gap-1.5" role="group" aria-label="Filtrar por modalidad">
          <span className="mr-1 text-[10px] font-semibold uppercase opacity-50">
            Modalidad
          </span>
          {MODALIDAD_CHIPS.map((c) => (
            <FilterChip
              key={c.value || 'todos'}
              active={modalidad === c.value}
              onClick={() => onModalidadChange(modalidad === c.value ? '' : c.value)}
            >
              {c.label}
            </FilterChip>
          ))}
        </div>
        <div className="flex flex-wrap items-center gap-1.5" role="group" aria-label="Filtrar por estado">
          <span className="mr-1 text-[10px] font-semibold uppercase opacity-50">
            Estado
          </span>
          {ESTADO_CHIPS.map((c) => (
            <FilterChip
              key={c.value || 'todos'}
              active={estado === c.value}
              onClick={() => onEstadoChange(estado === c.value ? '' : c.value)}
            >
              {c.label}
            </FilterChip>
          ))}
        </div>
      </div>

      {/* Contador + limpiar filtros */}
      <div className="mt-3 flex items-center justify-between gap-3 border-t pt-3" style={{ borderColor: '#DFE5ED' }}>
        <p className="text-[11px] opacity-60" role="status" aria-live="polite">
          {counterLabel}
          {hasActiveFilters && filteredCount !== totalCount && (
            <span className="opacity-70"> de {totalCount}</span>
          )}
          {hasActiveFilters && (
            <span className="ml-2 opacity-70">· filtros activos</span>
          )}
        </p>
        {hasActiveFilters && (
          <button
            type="button"
            onClick={onClearFilters}
            className="flex shrink-0 items-center gap-1 text-[11px] font-semibold"
            style={{ color: '#557EFF' }}
            aria-label="Limpiar filtros"
          >
            <X className="h-3 w-3" />
            Limpiar filtros
          </button>
        )}
      </div>
    </div>
  );
}
