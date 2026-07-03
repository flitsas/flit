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
  | 'rechazado';

export const ESTADOS_TRAMITE: readonly EstadoTramite[] = [
  'borrador',
  'anulado',
  'preparado',
  'entregado',
  'aprobado',
  'rechazado',
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
};

export interface EstadoChipStyle {
  bg: string;
  color: string;
  border: string;
}

/** Paleta coherente con los chips existentes de TramitesTable (ámbar/azul/etc.). */
export const ESTADO_CHIP_STYLES: Record<EstadoTramite, EstadoChipStyle> = {
  borrador: {
    bg: 'rgba(245,158,11,0.12)',
    color: '#b45309',
    border: 'rgba(245,158,11,0.3)',
  },
  preparado: {
    bg: 'rgba(139,92,246,0.12)',
    color: '#7c3aed',
    border: 'rgba(139,92,246,0.3)',
  },
  entregado: {
    bg: 'rgba(85,126,255,0.12)',
    color: '#557eff',
    border: 'rgba(85,126,255,0.3)',
  },
  aprobado: {
    bg: 'rgba(34,197,94,0.12)',
    color: '#15803d',
    border: 'rgba(34,197,94,0.3)',
  },
  rechazado: {
    bg: 'rgba(255,78,0,0.10)',
    color: '#c2410c',
    border: 'rgba(255,78,0,0.3)',
  },
  anulado: {
    bg: 'rgba(100,116,139,0.12)',
    color: '#475569',
    border: 'rgba(100,116,139,0.3)',
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
  };
}
