'use client';

import {
  ESTADO_CHIP_STYLES,
  ESTADO_ICONO,
  ESTADO_LABELS,
  FILTRO_RECHAZADO_PREASIGNACION,
  RECHAZADO_PREASIGNACION_LABEL,
  type EstadoFiltro,
} from '@/lib/tramites/estados';
import { FranjaEstados, type FranjaItem } from './FranjaEstados';
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
  const items: FranjaItem[] = estados.map((estado) => {
    const label = labelDe(estado);
    const count = counts[estado] ?? 0;
    const style = estiloDe(estado);
    return {
      key: estado,
      label,
      icon: iconoDe(estado),
      count,
      ariaLabel: `${label}: ${count} trámite${count === 1 ? '' : 's'}`,
      activeBg: style.bg,
      activeColor: style.color,
    };
  });

  return (
    <FranjaEstados
      items={items}
      selected={selected}
      onSelect={(key) => onSelect?.(key as EstadoFiltro | '')}
      ariaLabel="Estados de los trámites"
    />
  );
}
