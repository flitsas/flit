'use client';

import { ChevronRight } from 'lucide-react';
import {
  ESTADO_CHIP_STYLES,
  ESTADO_LABELS,
  type EstadoTramite,
} from '@/lib/tramites/estados';
import type { InstanceStatus } from '@/lib/api/types/procedure-runtime';

/**
 * Funnel de estados del listado de trámites (paridad con el diseño flit-2.0-main):
 * una fila de tarjetas por estado de negocio con su conteo y color, que además
 * actúa como filtro (toggle) por estado. La fuente de labels/colores es
 * `lib/tramites/estados.ts` (los 6 estados de negocio, ADR-0022).
 */

// Orden de embudo (ciclo de vida): borrador → preparado → entregado → aprobado,
// con los desenlaces (rechazado/anulado) al final.
const FUNNEL_ORDER: EstadoTramite[] = [
  'borrador',
  'preparado',
  'entregado',
  // Ruta de preasignación de placa (Feature #10587).
  'preasignado',
  'asignado',
  'aprobado',
  'rechazado',
  'anulado',
];

export interface EstadoFunnelProps {
  /** Conteo por estado (calculado sobre el total de trámites). */
  counts: Record<EstadoTramite, number>;
  /** Estado activo del filtro ('' = todos). */
  active: '' | InstanceStatus;
  /** Alterna el filtro por estado. */
  onSelect: (estado: '' | InstanceStatus) => void;
}

export function EstadoFunnel({ counts, active, onSelect }: EstadoFunnelProps) {
  return (
    <div
      role="group"
      aria-label="Estados de los trámites"
      className="grid grid-cols-2 gap-2 sm:grid-cols-4 lg:grid-cols-8"
    >
      {FUNNEL_ORDER.map((estado, i) => {
        const style = ESTADO_CHIP_STYLES[estado];
        const isActive = active === estado;
        return (
          <button
            key={estado}
            type="button"
            aria-label={ESTADO_LABELS[estado]}
            aria-pressed={isActive}
            onClick={() => onSelect(isActive ? '' : estado)}
            className="relative rounded-xl border border-[#DFE5ED] bg-white p-3 text-center transition hover:border-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14]"
            style={
              isActive
                ? { borderColor: style.color, background: style.bg }
                : undefined
            }
          >
            <p className="truncate text-[10px] font-medium opacity-70">
              {ESTADO_LABELS[estado]}
            </p>
            <p className="mt-1 text-xl font-bold" style={{ color: style.color }}>
              {counts[estado] ?? 0}
            </p>
            {i < FUNNEL_ORDER.length - 1 && (
              <ChevronRight
                className="absolute -right-[9px] top-1/2 hidden h-3 w-3 -translate-y-1/2 opacity-30 lg:block"
                aria-hidden="true"
              />
            )}
          </button>
        );
      })}
    </div>
  );
}
