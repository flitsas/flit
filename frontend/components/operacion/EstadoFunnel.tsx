'use client';

import {
  BadgeCheck,
  Ban,
  CheckCircle2,
  FileCheck2,
  FileText,
  Sparkles,
  XCircle,
} from 'lucide-react';
import {
  ESTADO_CHIP_STYLES,
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

const ESTADO_ICON: Record<EstadoTramite, typeof FileText> = {
  borrador: FileText,
  preparado: FileCheck2,
  entregado: BadgeCheck,
  aprobado: CheckCircle2,
  subsanacion: Sparkles,
  rechazado: XCircle,
  anulado: Ban,
};

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
        const Icon = ESTADO_ICON[estado];
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
            <span
              className="grid h-7 w-7 shrink-0 place-items-center rounded-full"
              style={{ background: `${style.accent}1F` }}
            >
              <Icon className="h-3.5 w-3.5" style={{ color: style.accent }} aria-hidden="true" />
            </span>
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
