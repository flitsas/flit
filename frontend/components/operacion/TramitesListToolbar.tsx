'use client';

import { RefreshCw, Star } from 'lucide-react';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * Barra de tipo de trámite del listado (Track A). Tabs con subrayado por modalidad +
 * acciones de vista (prioritarios / actualizar), en el estilo de la pantalla principal
 * de trámites. El filtro por estado vive en la tira de KPIs (EstadoFunnel); compañía,
 * periodo, filtros específicos, búsqueda y columnas viven en `TramitesFiltrosBar`, la
 * tarjeta siempre visible que va justo debajo de esta fila. Es presentacional.
 *
 * Nota: el diseño dibuja además una pestaña "Otros trámites". No se incluye porque hoy
 * `WizardModalidad` solo tiene matrícula inicial y traspaso: una pestaña que no filtra
 * nada sería un control muerto. Entra cuando exista la modalidad en el backend.
 */
interface Props {
  modalidad: '' | WizardModalidad;
  onModalidadChange: (v: '' | WizardModalidad) => void;
  onRefresh: () => void;
  loading?: boolean;
  /** ¿Hay algún filtro activo (búsqueda/modalidad/estado/compañía)? Lo calcula el contenedor. */
  hasActiveFilters: boolean;
  /** HU #10536 — filtro "solo prioritarios". */
  soloPrioritarios: boolean;
  onPrioritariosChange: (v: boolean) => void;
}

const MODALIDAD_TABS: { value: '' | WizardModalidad; label: string }[] = [
  { value: '', label: 'Todos' },
  { value: 'matricula_inicial', label: 'Matrícula inicial' },
  { value: 'traspaso', label: 'Traspaso' },
];

export function TramitesListToolbar({
  modalidad,
  onModalidadChange,
  onRefresh,
  loading = false,
  hasActiveFilters,
  soloPrioritarios,
  onPrioritariosChange,
}: Props) {
  // El RECUENTO ya no se anuncia aquí: lo dice la píldora visible que hay sobre la tabla, que es
  // una región `status` por sí misma. Aquí queda solo el estado del filtrado, que no tiene
  // equivalente visible en esta fila.
  const estadoFiltrado = [
    hasActiveFilters ? 'filtros activos' : null,
    soloPrioritarios ? 'solo prioritarios' : null,
  ]
    .filter(Boolean)
    .join(' · ');

  return (
    <div className="flex min-w-0 flex-col">
      {/* Tabs por modalidad + acciones de vista, DIRECTAMENTE sobre el fondo de la página: en el
          diseño esta fila no vive dentro de una tarjeta, solo la separa un borde inferior. El tab
          activo se marca con color Y con subrayado: el estado no depende solo del color. */}
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[#DFE5ED] dark:border-white/10">
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
                style={active ? { color: '#557EFF' } : { color: '#162744', opacity: 0.65 }}
              >
                {t.label}
                {active ? (
                  <span
                    className="absolute inset-x-2 -bottom-px h-0.5 rounded-full"
                    style={{ background: '#557EFF' }}
                    aria-hidden="true"
                  />
                ) : null}
              </button>
            );
          })}
        </div>
        <div className="flex flex-wrap items-center gap-1 pb-1">
          {/* HU #10536 — filtro "solo prioritarios". */}
          <button
            type="button"
            onClick={() => onPrioritariosChange(!soloPrioritarios)}
            aria-pressed={soloPrioritarios}
            aria-label="Mostrar solo trámites prioritarios"
            title={soloPrioritarios ? 'Mostrando solo prioritarios' : 'Mostrar solo prioritarios'}
            className="rounded-lg p-2 transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          >
            <Star
              className="h-4 w-4"
              style={
                soloPrioritarios
                  ? { color: '#F59E0B', fill: '#F59E0B' }
                  : { color: '#162744', opacity: 0.45 }
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
            className="rounded-lg p-2 transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50"
          >
            <RefreshCw
              className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`}
              style={{ color: '#162744', opacity: 0.55 }}
              aria-hidden="true"
            />
          </button>
        </div>
      </div>

      {/* Región viva SOLO para el estado del filtrado: que haya filtros aplicados o el modo "solo
          prioritarios" no se lee en ningún sitio de esta fila. El recuento se quitó de aquí al
          hacerse visible sobre la tabla — dos regiones `status` diciendo lo mismo se anunciaban
          por duplicado en cada cambio de filtro. */}
      <p className="sr-only" role="status" aria-live="polite">
        {estadoFiltrado}
      </p>
    </div>
  );
}
