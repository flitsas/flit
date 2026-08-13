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
import type { InstanceStatus } from '@/lib/api/types/procedure-runtime';

/**
 * Tira de KPIs por estado de la pantalla principal de trámites: una tarjeta única
 * dividida en columnas, con icono, etiqueta y conteo por estado, que además actúa
 * como filtro (toggle). La fuente de labels/colores es `lib/tramites/estados.ts`.
 *
 * Sustituye al embudo de 6 tarjetas sueltas: mismo rol funcional, presentación del
 * diseño vigente. Incorpora `subsanacion`, que el embudo anterior omitía pese a
 * estar en el vocabulario de estados desde la HU #10870/#10874.
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
      className="grid grid-cols-2 divide-[#EEF2F7] overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white sm:grid-cols-4 sm:divide-x lg:grid-cols-7 dark:divide-white/5 dark:border-white/10 dark:bg-[#162744]"
    >
      {FUNNEL_ORDER.map((estado) => {
        const style = ESTADO_CHIP_STYLES[estado];
        const Icon = ESTADO_ICON[estado];
        const isActive = active === estado;
        const label = ESTADO_LABELS[estado];
        const count = counts[estado] ?? 0;
        return (
          <button
            key={estado}
            type="button"
            aria-label={`${label}: ${count} trámite${count === 1 ? '' : 's'}`}
            aria-pressed={isActive}
            onClick={() => onSelect(isActive ? '' : estado)}
            className="flex flex-col items-center gap-1 px-2 py-3 transition hover:bg-[#557EFF]/[0.06] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF]"
            style={isActive ? { background: style.bg } : undefined}
          >
            {/* Icono: elemento gráfico (umbral 3:1), así que lleva el tono PURO del diseño.
                El texto del chip usa la variante oscurecida — ver ESTADO_CHIP_STYLES. */}
            <span
              className="grid h-8 w-8 shrink-0 place-items-center rounded-full"
              style={{ background: style.bg }}
            >
              <Icon className="h-4 w-4" style={{ color: style.accent }} aria-hidden="true" />
            </span>
            <span className="max-w-full truncate text-xs font-medium text-[#162744]/70 dark:text-white/70">
              {label}
            </span>
            <span
              className="text-xl font-bold leading-none tabular-nums text-[#162744] dark:text-white"
              aria-hidden="true"
            >
              {count}
            </span>
            {/* Indicador de filtro activo que no depende solo del color de fondo. */}
            <span
              className="h-0.5 w-6 rounded-full"
              style={{ background: isActive ? style.color : 'transparent' }}
              aria-hidden="true"
            />
          </button>
        );
      })}
    </div>
  );
}
