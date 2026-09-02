'use client';

import {
  ESTADO_CHIP_STYLES,
  ESTADO_ICONO,
  ESTADO_LABELS,
  type EstadoTramite,
} from '@/lib/tramites/estados';
/**
 * Tira de KPIs por estado de la pantalla principal de trámites: una tarjeta única
 * dividida en columnas, con icono, etiqueta y conteo. Clic en una columna filtra
 * el listado; segundo clic en la misma columna quita el filtro.
 * Labels/colores desde `lib/tramites/estados.ts`.
 */

// Orden del ciclo de vida: borrador → preparado → entregado → aprobado, con la
// reapertura (subsanación) y los desenlaces (rechazado/anulado) al final.
// La ruta de placa (Feature #10587 / HU #10785) NO añade tarjetas: su progreso es un sub-estado
// interno que vive bajo 'entregado' (se muestra como badge secundario en la fila).
const FUNNEL_ORDER: EstadoTramite[] = [
  'borrador',
  'preparado',
  'entregado',
  'aprobado',
  'subsanacion',
  'rechazado',
  'anulado',
];

export interface EstadoFunnelProps {
  /** Conteo por estado (calculado sobre el total de trámites). */
  counts: Record<EstadoTramite, number>;
  /** Estado actualmente filtrado; vacío = todos. */
  selected?: EstadoTramite | '';
  onSelect?: (estado: EstadoTramite | '') => void;
}

/** Tira de KPIs clicable: el filtro por estado vive aquí, no en "+ Filtro". */
export function EstadoFunnel({ counts, selected = '', onSelect }: EstadoFunnelProps) {
  return (
    <div
      role="group"
      aria-label="Estados de los trámites"
      className="grid grid-cols-2 divide-[#EEF2F7] overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white shadow-[0_4px_12px_rgba(0,0,0,0.04)] sm:grid-cols-4 sm:divide-x lg:grid-cols-7 dark:divide-white/5 dark:border-white/10 dark:bg-[#162744]"
    >
      {FUNNEL_ORDER.map((estado) => {
        const style = ESTADO_CHIP_STYLES[estado];
        const label = ESTADO_LABELS[estado];
        const count = counts[estado] ?? 0;
        const activo = selected === estado;
        return (
          <button
            key={estado}
            type="button"
            aria-label={`${label}: ${count} trámite${count === 1 ? '' : 's'}`}
            aria-pressed={activo}
            onClick={() => onSelect?.(activo ? '' : estado)}
            className="flex flex-col items-center gap-1 px-2 py-2 transition hover:bg-[#557EFF]/[0.06] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF]"
            style={activo ? { background: style.bg } : undefined}
          >
            {/* El SVG ya trae su círculo de color: se pinta entero, sin pastilla tintada
                alrededor ni recoloreado por CSS. Decorativo — el nombre accesible del botón ya
                dice el estado y el conteo. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={ESTADO_ICONO[estado]}
              alt=""
              aria-hidden="true"
              width={28}
              height={28}
              className="h-7 w-7 shrink-0"
            />
            <span className="max-w-full truncate text-xs font-medium opacity-70 text-[#162744] dark:text-white/70">
              {label}
            </span>
            <span
              className="text-lg font-bold leading-none tabular-nums text-[#1E293B] dark:text-white"
              aria-hidden="true"
            >
              {count}
            </span>
            <span
              className="h-0.5 w-6 rounded-full"
              style={{ background: activo ? style.color : 'transparent' }}
              aria-hidden="true"
            />
          </button>
        );
      })}
    </div>
  );
}
