'use client';

import {
  ESTADO_CHIP_STYLES,
  ESTADO_ICONO,
  ESTADO_LABELS,
  FILTRO_RECHAZADO_PREASIGNACION,
  RECHAZADO_PREASIGNACION_LABEL,
  type EstadoFiltro,
} from '@/lib/tramites/estados';
/**
 * Tira de KPIs por estado de la pantalla principal de trámites: una tarjeta única
 * dividida en columnas, con icono, etiqueta y conteo. Clic en una columna filtra
 * el listado; segundo clic en la misma columna quita el filtro.
 * Labels/colores desde `lib/tramites/estados.ts`.
 */

// Orden del ciclo de vida: borrador → preparado → preasignación → asignado → entregado →
// aprobado, con la reapertura (subsanación), los desenlaces (rechazado, rechazado desde
// preasignación, revocado, anulado) al final.
//
// ADR-0059 — la ruta de placa son estados reales y por eso tienen tarjeta. «Rechazado
// preasignación» no es un estado sino un pseudo-filtro (rechazado + rejectedFrom): el gestor lo
// pidió como tarjeta propia para priorizar los rechazos que hay que volver a mandar a la cola de
// placa. Comparte icono y color con Rechazado a propósito: sigue siendo un rechazo.
const FUNNEL_ORDER: readonly EstadoFiltro[] = [
  'borrador',
  'preparado',
  'preasignacion',
  'asignado',
  'entregado',
  'aprobado',
  'subsanacion',
  'rechazado',
  FILTRO_RECHAZADO_PREASIGNACION,
  'revocado',
  'anulado',
];

// Columnas en pantalla ancha: una por tarjeta. Clases literales para que Tailwind las genere.
const XL_COLS: Record<number, string> = {
  4: 'xl:grid-cols-4',
  5: 'xl:grid-cols-5',
  6: 'xl:grid-cols-6',
  7: 'xl:grid-cols-7',
  8: 'xl:grid-cols-8',
  9: 'xl:grid-cols-9',
  10: 'xl:grid-cols-10',
  11: 'xl:grid-cols-11',
};

function estiloDe(estado: EstadoFiltro) {
  return estado === FILTRO_RECHAZADO_PREASIGNACION
    ? ESTADO_CHIP_STYLES.rechazado
    : ESTADO_CHIP_STYLES[estado];
}

function iconoDe(estado: EstadoFiltro) {
  return estado === FILTRO_RECHAZADO_PREASIGNACION ? ESTADO_ICONO.rechazado : ESTADO_ICONO[estado];
}

function labelDe(estado: EstadoFiltro) {
  return estado === FILTRO_RECHAZADO_PREASIGNACION
    ? RECHAZADO_PREASIGNACION_LABEL
    : ESTADO_LABELS[estado];
}

export interface EstadoFunnelProps {
  /** Conteo por estado (calculado sobre el total de trámites). El pseudo-estado puede faltar (0). */
  counts: Partial<Record<EstadoFiltro, number>>;
  /** Estado actualmente filtrado; vacío = todos. */
  selected?: EstadoFiltro | '';
  onSelect?: (estado: EstadoFiltro | '') => void;
  /**
   * Epic #12686 — tarjetas a pintar, en orden (ver `lib/tramites/panelesEstado.ts`). Sin valor se
   * pintan las 11 de siempre.
   */
  estados?: readonly EstadoFiltro[];
}

/** Tira de KPIs clicable: el filtro por estado vive aquí, no en "+ Filtro". */
export function EstadoFunnel({
  counts,
  selected = '',
  onSelect,
  estados = FUNNEL_ORDER,
}: EstadoFunnelProps) {
  return (
    <div
      role="group"
      aria-label="Estados de los trámites"
      className={`grid grid-cols-2 divide-[#EEF2F7] overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white shadow-[0_4px_12px_rgba(0,0,0,0.04)] sm:grid-cols-4 sm:divide-x lg:grid-cols-6 ${XL_COLS[estados.length] ?? 'xl:grid-cols-11'} dark:divide-white/5 dark:border-white/10 dark:bg-[#162744]`}
    >
      {estados.map((estado) => {
        const style = estiloDe(estado);
        const label = labelDe(estado);
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
              src={iconoDe(estado)}
              alt=""
              aria-hidden="true"
              width={28}
              height={28}
              className="h-7 w-7 shrink-0"
            />
            {/* Hasta dos líneas: «Rechazado preasignación» no cabe en una y truncarlo dejaba
                «Rechazado pre…», que no se distingue de la tarjeta de al lado. */}
            <span className="line-clamp-2 max-w-full text-center text-xs font-medium leading-tight opacity-70 text-[#162744] dark:text-white/70">
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
