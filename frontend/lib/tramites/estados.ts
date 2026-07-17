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

/**
 * Sub-estado INTERNO de la ruta de placa (Feature #10587 / HU #10785), ORTOGONAL al estado del
 * trámite: mientras avanza, el trámite permanece en `entregado`. Se muestra como un badge secundario
 * junto al chip de estado. `null` = trámite sin ruta de placa.
 */
export type PlateFlowStatus = 'preasignado' | 'asignado';

export const PLATE_FLOW_STATUSES: readonly PlateFlowStatus[] = ['preasignado', 'asignado'] as const;

/** Etiqueta del badge secundario (describe el progreso de la placa, no el estado del trámite). */
export const PLATE_FLOW_LABELS: Record<PlateFlowStatus, string> = {
  preasignado: 'En cola del OT',
  asignado: 'Con placa',
};

/** Colores del badge de sub-estado: preasignado = cian (en cola del OT); asignado = índigo (con placa). */
export const PLATE_FLOW_CHIP_STYLES: Record<PlateFlowStatus, EstadoChipStyle> = {
  preasignado: {
    bg: 'rgba(6,182,212,0.12)',
    color: '#0e7490',
    border: 'rgba(6,182,212,0.3)',
  },
  asignado: {
    bg: 'rgba(99,102,241,0.12)',
    color: '#4f46e5',
    border: 'rgba(99,102,241,0.3)',
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
 * HU #10804 (Feature #10587) — ¿el OT puede DECIDIR (Aprobar/Rechazar) este trámite?
 *
 * - Ruta estándar (sin sub-estado de placa): sí, como siempre.
 * - Ruta de placa: solo cuando la placa está `asignado` **y** el SOAT está `vigente`. Mientras esté
 *   `preasignado` (falta asignar placa) o `asignado` sin SOAT vigente (el gestor aún no validó por
 *   RUNT ni cargó el PDF), Aprobar y Rechazar se ocultan juntos. Espeja el gate DURO del backend
 *   (SoatGate), que de todos modos rechazaría la aprobación por API.
 */
export function puedeDecidirOt(
  plateFlowStatus: string | null | undefined,
  soatEstado: string | null | undefined,
): boolean {
  if (!esPlateFlowStatus(plateFlowStatus)) return true; // ruta estándar
  return plateFlowStatus === 'asignado' && soatEstado === 'vigente';
}

/** ¿El trámite está en ruta de placa `asignado` pero aún sin SOAT vigente (esperando al gestor)? */
export function esperandoSoatDelGestor(
  plateFlowStatus: string | null | undefined,
  soatEstado: string | null | undefined,
): boolean {
  return plateFlowStatus === 'asignado' && soatEstado !== 'vigente';
}
