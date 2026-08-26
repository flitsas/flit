'use client';

import type { ReactNode } from 'react';
import { RefreshCw, Star } from 'lucide-react';
import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';
import { FAMILIA_OPCIONES } from '@/lib/api/types/familia-labels';

/**
 * Barra de tipo de trámite del listado (`flit-tramites-chrome`). Tabs con subrayado
 * alineado al borde inferior + slot de filtros (búsqueda, Periodo, + Filtro, Columnas)
 * + prioritarios + actualizar, en la misma fila. Presentacional.
 */
interface Props {
  /** Familia seleccionada; cadena vacía = todas. */
  modalidad: '' | ProcedureFamily;
  onModalidadChange: (v: '' | ProcedureFamily) => void;
  onRefresh: () => void;
  loading?: boolean;
  /** ¿Hay algún filtro activo (búsqueda/modalidad/estado/compañía)? Lo calcula el contenedor. */
  hasActiveFilters: boolean;
  /** HU #10536 — filtro "solo prioritarios". */
  soloPrioritarios: boolean;
  onPrioritariosChange: (v: boolean) => void;
  /** Búsqueda, Periodo, + Filtro, Columnas — a la derecha de los tabs. */
  actions?: ReactNode;
}

const MODALIDAD_TABS: { value: '' | ProcedureFamily; label: string }[] = [
  { value: '', label: 'Todos' },
  ...FAMILIA_OPCIONES,
];

const CONTROL_CLS =
  'inline-flex h-9 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-xl border border-[#DFE5ED] bg-white px-3 text-xs font-semibold text-[#1E293B] transition hover:bg-[#EFF6FF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-[#0B0F14] dark:text-white';

export function TramitesListToolbar({
  modalidad,
  onModalidadChange,
  onRefresh,
  loading = false,
  hasActiveFilters,
  soloPrioritarios,
  onPrioritariosChange,
  actions,
}: Props) {
  const estadoFiltrado = [
    hasActiveFilters ? 'filtros activos' : null,
    soloPrioritarios ? 'solo prioritarios' : null,
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <div className="flex min-w-0 flex-col">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[#DFE5ED] pb-2 dark:border-white/10">
        <div
          className="flex flex-wrap items-center gap-1"
          role="tablist"
          aria-label="Tipo de trámite"
        >
          {MODALIDAD_TABS.map((t) => {
            const active = modalidad === t.value;
            return (
              <button
                key={t.value || 'todos'}
                type="button"
                role="tab"
                aria-selected={active}
                onClick={() => onModalidadChange(t.value)}
                className="relative rounded-t-lg px-4 py-2.5 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF]"
                style={active ? { color: '#557EFF', opacity: 1 } : { color: '#162744', opacity: 0.65 }}
              >
                {t.label}
                {active ? (
                  <span
                    className="absolute inset-x-2 -bottom-2.5 h-0.5 rounded-full bg-[#557EFF]"
                    aria-hidden="true"
                  />
                ) : null}
              </button>
            );
          })}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {actions}
          <button
            type="button"
            onClick={() => onPrioritariosChange(!soloPrioritarios)}
            aria-pressed={soloPrioritarios}
            aria-label="Mostrar solo trámites prioritarios"
            title={soloPrioritarios ? 'Mostrando solo prioritarios' : 'Mostrar solo prioritarios'}
            className="grid h-9 w-9 shrink-0 place-items-center rounded-xl border border-[#DFE5ED] bg-white transition hover:bg-[#EFF6FF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-[#0B0F14]"
          >
            <Star
              className="h-4 w-4"
              style={
                soloPrioritarios
                  ? { color: '#F9AC00', fill: '#F9AC00' }
                  : { color: '#94A3B8', fill: 'transparent' }
              }
              aria-hidden="true"
            />
          </button>
          <button
            type="button"
            onClick={onRefresh}
            disabled={loading}
            aria-label="Actualizar listado de trámites"
            title="Actualizar"
            className={`${CONTROL_CLS} disabled:cursor-not-allowed disabled:opacity-50`}
          >
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} aria-hidden="true" />
            Actualizar
          </button>
        </div>
      </div>

      <p className="sr-only" role="status" aria-live="polite">
        {estadoFiltrado}
      </p>
    </div>
  );
}
