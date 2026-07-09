'use client';

import { RefreshCw, Star, X } from 'lucide-react';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * Barra de filtros del listado de trámites (Track A). Chips de modalidad +
 * Actualizar + contador/limpiar, en el estilo FLIT del módulo. El filtro por
 * estado vive en el funnel de estados (EstadoFunnel) y la búsqueda desplegable
 * en la fila de acciones (ambos en TramitesTable). Es presentacional.
 */
interface Props {
  modalidad: '' | WizardModalidad;
  onModalidadChange: (v: '' | WizardModalidad) => void;
  onRefresh: () => void;
  onClearFilters: () => void;
  loading?: boolean;
  /** ¿Hay algún filtro activo (búsqueda/modalidad/estado/compañía)? Lo calcula el contenedor. */
  hasActiveFilters: boolean;
  totalCount: number;
  filteredCount: number;
  /** HU #10536 — filtro "solo prioritarios". */
  soloPrioritarios: boolean;
  onPrioritariosChange: (v: boolean) => void;
}

const MODALIDAD_CHIPS: { value: '' | WizardModalidad; label: string }[] = [
  { value: '', label: 'Todos' },
  { value: 'matricula_inicial', label: 'Matrícula inicial' },
  { value: 'traspaso', label: 'Traspaso' },
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
          : { color: '#162744' }
      }
    >
      {children}
    </button>
  );
}

export function TramitesListToolbar({
  modalidad,
  onModalidadChange,
  onRefresh,
  onClearFilters,
  loading = false,
  hasActiveFilters,
  totalCount,
  filteredCount,
  soloPrioritarios,
  onPrioritariosChange,
}: Props) {
  const counterLabel =
    filteredCount === 0
      ? 'Sin resultados'
      : `${filteredCount} trámite${filteredCount === 1 ? '' : 's'}`;

  return (
    <div
      className="rounded-2xl border bg-white p-4 dark:bg-[#0B0F14]"
    >
      {/* Filtro por modalidad + Actualizar (el filtro por estado vive en el funnel
          de estados y la búsqueda en la fila de acciones de arriba). */}
      <div className="flex flex-wrap items-center gap-2">
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
        {/* HU #10536 — filtro "solo prioritarios". */}
        <div className="flex items-center gap-1.5" role="group" aria-label="Filtrar por prioridad">
          <FilterChip
            active={soloPrioritarios}
            onClick={() => onPrioritariosChange(!soloPrioritarios)}
          >
            <span className="inline-flex items-center gap-1">
              <Star
                className="h-3 w-3"
                style={soloPrioritarios ? { fill: 'currentColor' } : undefined}
                aria-hidden="true"
              />
              Prioritarios
            </span>
          </FilterChip>
        </div>
        <button
          type="button"
          onClick={onRefresh}
          disabled={loading}
          className="ml-auto flex shrink-0 items-center justify-center gap-1.5 rounded-xl border px-4 py-2 text-[11px] font-semibold disabled:opacity-50"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
          aria-label="Actualizar listado de trámites"
        >
          <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} />
          Actualizar
        </button>
      </div>

      {/* Contador + limpiar filtros */}
      <div className="mt-3 flex items-center justify-between gap-3 border-t pt-3">
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
