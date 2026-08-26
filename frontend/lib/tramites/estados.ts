/**
 * Estados de NEGOCIO del ciclo de vida del trámite (N 03, RF01 — ADR-0022 backend).
 * Vocabulario único persistido/expuesto por la API. Este módulo es la fuente de verdad
 * de labels y colores de chips para TODO el frontend (timeline, badges del listado, wizard).
 */

export type EstadoTramite =
  | 'borrador'
  | 'anulado'
  | 'preparado'
  | 'entregado'
  | 'aprobado'
  | 'rechazado'
  // HU #10870/#10874 — reabre la edición de un entregado/rechazado sin volver a borrador.
  | 'subsanacion';

export const ESTADOS_TRAMITE: readonly EstadoTramite[] = [
  'borrador',
  'anulado',
  'preparado',
  'entregado',
  'aprobado',
  'rechazado',
  'subsanacion',
] as const;

/** Estados finales (RF04): sin transiciones posteriores ni edición. */
export const ESTADOS_FINALES: readonly EstadoTramite[] = ['aprobado', 'anulado'] as const;

export const ESTADO_LABELS: Record<EstadoTramite, string> = {
  borrador: 'Borrador',
  anulado: 'Anulado',
  preparado: 'Preparado',
  entregado: 'Entregado',
  aprobado: 'Aprobado',
  rechazado: 'Rechazado',
  subsanacion: 'En subsanación',
};

export interface EstadoChipStyle {
  bg: string;
  /** Color del TEXTO del chip. Cumple ≥4.5:1 sobre `bg`: es texto pequeño. */
  color: string;
  border: string;
  /**
   * Tono puro del estado, tal cual lo define el diseño. Para elementos GRÁFICOS (icono de la
   * tarjeta KPI, puntos, barras), donde el umbral es 3:1 y sí se puede usar el color saturado.
   * No usarlo como color de texto: varios de estos tonos no llegan a 4.5:1 sobre blanco.
   */
  accent: string;
}

/**
 * Paleta de estados del diseño de la pantalla de trámites. Cada estado tiene su propio tono —
 * gris, azul, violeta, verde, cian, naranja y vino — en vez de reutilizar una escala de cinco.
 *
 * `accent` es el tono exacto del diseño; `color` es ese mismo tono oscurecido lo justo para que
 * el texto del chip llegue a 4.5:1. Varios de los tonos puros se quedan cortos como texto
 * (#557EFF ≈ 3.7:1, #16A34A ≈ 3.1:1, #FF4E00 ≈ 3.5:1, #00DBD5 muy por debajo), así que se
 * conserva la identidad cromática y se ajusta solo la luminosidad, que es lo que corresponde
 * cuando un color del prototipo no cumple contraste en un uso concreto.
 */
export const ESTADO_CHIP_STYLES: Record<EstadoTramite, EstadoChipStyle> = {
  borrador: {
    bg: 'rgba(148,163,184,0.16)',
    color: '#64748B',
    border: 'rgba(148,163,184,0.35)',
    accent: '#94A3B8',
  },
  preparado: {
    bg: 'rgba(85,126,255,0.14)',
    color: '#3B4FD6',
    border: 'rgba(85,126,255,0.35)',
    accent: '#557EFF',
  },
  entregado: {
    bg: 'rgba(139,92,246,0.14)',
    color: '#6D28D9',
    border: 'rgba(139,92,246,0.35)',
    accent: '#7C3AED',
  },
  aprobado: {
    bg: 'rgba(34,197,94,0.14)',
    color: '#15803D',
    border: 'rgba(34,197,94,0.35)',
    accent: '#16A34A',
  },
  rechazado: {
    bg: 'rgba(255,78,0,0.14)',
    color: '#C2410C',
    border: 'rgba(255,78,0,0.32)',
    accent: '#FF4E00',
  },
  anulado: {
    bg: 'rgba(153,27,27,0.14)',
    color: '#991B1B',
    border: 'rgba(153,27,27,0.32)',
    accent: '#991B1B',
  },
  subsanacion: {
    bg: 'rgba(0,219,213,0.16)',
    color: '#0F766E',
    border: 'rgba(0,219,213,0.35)',
    accent: '#00DBD5',
  },
};

function esEstadoTramite(value: string): value is EstadoTramite {
  return (ESTADOS_TRAMITE as readonly string[]).includes(value);
}

/**
 * Label del estado con fallback al valor crudo en titlecase: tolera el vocabulario
 * viejo (draft/submitted/…) durante la transición de datos sin romper el render.
 */
export function estadoLabel(value: string | null | undefined): string {
  if (!value) return '—';
  if (esEstadoTramite(value)) return ESTADO_LABELS[value];
  const text = value.replace(/_/g, ' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** Estilo del chip con fallback neutro (gris) para estados desconocidos/antiguos. */
export function estadoChipStyle(value: string | null | undefined): EstadoChipStyle {
  if (value && esEstadoTramite(value)) return ESTADO_CHIP_STYLES[value];
  return {
    bg: 'rgba(100,116,139,0.12)',
    color: '#475569',
    border: 'rgba(100,116,139,0.3)',
    accent: '#64748B',
  };
}

/**
 * Sub-estado INTERNO de la ruta de placa. Ortogonal a `entregado`.
 * UI: Sin asignar / Asignado / Terminado.
 */
export type PlateFlowStatus = 'preasignado' | 'asignado' | 'terminado';

export const PLATE_FLOW_STATUSES: readonly PlateFlowStatus[] = [
  'preasignado',
  'asignado',
  'terminado',
] as const;

/** Etiqueta del badge secundario. */
export const PLATE_FLOW_LABELS: Record<PlateFlowStatus, string> = {
  preasignado: 'Sin asignar',
  asignado: 'Asignado',
  terminado: 'Terminado',
};

export const PLATE_FLOW_CHIP_STYLES: Record<PlateFlowStatus, EstadoChipStyle> = {
  preasignado: {
    bg: 'rgba(6,182,212,0.12)',
    color: '#0e7490',
    border: 'rgba(6,182,212,0.3)',
    accent: '#06B6D4',
  },
  asignado: {
    bg: 'rgba(99,102,241,0.12)',
    color: '#4f46e5',
    border: 'rgba(99,102,241,0.3)',
    accent: '#6366F1',
  },
  terminado: {
    bg: 'rgba(16,185,129,0.12)',
    color: '#047857',
    border: 'rgba(16,185,129,0.3)',
    accent: '#10B981',
  },
};

export function esPlateFlowStatus(value: string | null | undefined): value is PlateFlowStatus {
  return !!value && (PLATE_FLOW_STATUSES as readonly string[]).includes(value);
}

/** Label del badge de sub-estado de placa; `null` si el trámite no está en la ruta de placa. */
export function plateFlowLabel(value: string | null | undefined): string | null {
  return esPlateFlowStatus(value) ? PLATE_FLOW_LABELS[value] : null;
}

/** Estilo del badge de sub-estado de placa; `null` si el trámite no está en la ruta de placa. */
export function plateFlowChipStyle(value: string | null | undefined): EstadoChipStyle | null {
  return esPlateFlowStatus(value) ? PLATE_FLOW_CHIP_STYLES[value] : null;
}

/**
 * ¿El OT puede DECIDIR (Aprobar/Rechazar)?
 * - Ruta estándar (sin sub-estado): sí.
 * - Ruta de placa: solo en `terminado`.
 */
export function puedeDecidirOt(
  plateFlowStatus: string | null | undefined,
  _soatEstado?: string | null | undefined,
): boolean {
  if (!esPlateFlowStatus(plateFlowStatus)) return true;
  return plateFlowStatus === 'terminado';
}

/** ¿El gestor aún debe procesar (Asignado → Terminado)? */
export function esperandoProcesoDelGestor(
  plateFlowStatus: string | null | undefined,
): boolean {
  return plateFlowStatus === 'asignado';
}

/** @deprecated Usar esperandoProcesoDelGestor — se mantiene por compat de tests. */
export function esperandoSoatDelGestor(
  plateFlowStatus: string | null | undefined,
  _soatEstado?: string | null | undefined,
): boolean {
  return esperandoProcesoDelGestor(plateFlowStatus);
}
