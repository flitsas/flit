/**
 * Estados de NEGOCIO del ciclo de vida del trámite (N 03, RF01 — ADR-0022 backend).
 * Vocabulario único persistido/expuesto por la API. Este módulo es la fuente de verdad
 * de labels y colores de chips para TODO el frontend (timeline, badges del listado, wizard).
 */

export type EstadoTramite =
  | 'borrador'
  | 'anulado'
  | 'preparado'
  // ADR-0059 (Epic #12549) — Ruta Larga de matrícula: radicado sin placa, el OT debe asignarla.
  | 'preasignacion'
  // ADR-0059 — el OT ya asignó la placa; el gestor gestiona SOAT/impuestos y «Envía al OT».
  | 'asignado'
  | 'entregado'
  | 'aprobado'
  | 'rechazado'
  // HU #12166 — el OT deshizo su propia aprobación. Final.
  | 'revocado'
  // HU #10870/#10874 — reabre la edición de un entregado/rechazado sin volver a borrador.
  | 'subsanacion';

export const ESTADOS_TRAMITE: readonly EstadoTramite[] = [
  'borrador',
  'anulado',
  'preparado',
  'preasignacion',
  'asignado',
  'entregado',
  'aprobado',
  'rechazado',
  'revocado',
  'subsanacion',
] as const;

/** Estados finales (RF04): sin transiciones posteriores ni edición. */
export const ESTADOS_FINALES: readonly EstadoTramite[] = ['aprobado', 'anulado', 'revocado'] as const;

/**
 * ADR-0059 — estados de la RUTA DE PLACA: el trámite ya está en manos del organismo, pero todavía no
 * en su cola de decisión. Solo los alcanza un tipo que pide placa (matrícula inicial).
 */
export const ESTADOS_RUTA_PLACA: readonly EstadoTramite[] = ['preasignacion', 'asignado'] as const;

export const ESTADO_LABELS: Record<EstadoTramite, string> = {
  borrador: 'Borrador',
  anulado: 'Anulado',
  preparado: 'Preparado',
  preasignacion: 'Preasignación',
  asignado: 'Asignado',
  entregado: 'Entregado',
  aprobado: 'Aprobado',
  rechazado: 'Rechazado',
  revocado: 'Revocado',
  subsanacion: 'En subsanación',
};

/**
 * ADR-0059 — distintivo del rechazo desde Preasignación. El OT puede rechazar un trámite que todavía
 * no tenía placa; para el gestor es otra cola (hay que corregir y volver a pedir placa), así que el
 * chip lo dice sin cambiar de color: sigue siendo un rechazo.
 */
export const RECHAZADO_PREASIGNACION_LABEL = 'Rechazado preasignación';

/** Valor de `rejectedFrom` que activa el distintivo. */
export const REJECTED_FROM_PREASIGNACION = 'preasignacion';

/**
 * Pseudo-estado de FILTRO (no es un estado del trámite): «rechazado desde preasignación». El
 * servidor lo acepta en `estado=` y lo traduce a rechazado + rejectedFrom = preasignacion, y lo
 * cuenta aparte en `/instances/estado-counts`. Así la tira de KPIs lo ofrece como una tarjeta más.
 */
export const FILTRO_RECHAZADO_PREASIGNACION = 'rechazado_preasignacion';

/** Lo que el filtro de estado del gestor puede valer: un estado real o el pseudo-estado de arriba. */
export type EstadoFiltro = EstadoTramite | typeof FILTRO_RECHAZADO_PREASIGNACION;

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
 * gris pizarra, azul, verde azulado, verde lima, naranja, rojo y vino— en vez de reutilizar una
 * escala de cinco.
 *
 * El tono lo MANDA EL ICONO. Los diez iconos de estado (`public/assets/estados/*.svg`) traen su
 * círculo de color pintado dentro, así que `accent` es exactamente ese color: si el chip usara
 * otro, el mismo estado se vería de dos colores distintos en la misma pantalla —la tira de KPIs
 * y el chip de la fila— y un tono acabaría significando dos estados según dónde se mirara.
 *
 * `color` es ese mismo tono oscurecido lo justo para que el TEXTO del chip llegue a 4.5:1 sobre
 * `bg`. Casi ninguno de los tonos puros lo cumple como texto (#557EFF ≈ 3.7:1, #8CC63F ≈ 1.9:1,
 * #FF4E00 ≈ 3.5:1, #00A99D ≈ 2.7:1), así que se conserva la identidad cromática y se ajusta solo
 * la luminosidad, que es lo que corresponde cuando un color del prototipo no cumple contraste en
 * un uso concreto. `borrador` y `anulado` ya cumplen y se usan tal cual.
 *
 * ADR-0059: `preasignacion` es ámbar (#E08A00, el del artefacto de la epic: «falta algo del
 * organismo», a medio camino entre el azul de lo que avanza y el rojo de lo que se devolvió);
 * `asignado` es índigo (#6366F1), heredado del badge de placa asignada para que el usuario no
 * reaprenda; `revocado` (violeta) se separa de `anulado` (vino) porque son dos finales distintos:
 * uno lo decide el gestor, el otro lo deshace el organismo. Los mismos tonos valen para el
 * organismo: sus tarjetas y chips leen de aquí.
 */
export const ESTADO_CHIP_STYLES: Record<EstadoTramite, EstadoChipStyle> = {
  borrador: {
    bg: 'rgba(94,106,123,0.14)',
    color: '#5E6A7B',
    border: 'rgba(94,106,123,0.35)',
    accent: '#5E6A7B',
  },
  preparado: {
    bg: 'rgba(85,126,255,0.14)',
    color: '#4465CC',
    border: 'rgba(85,126,255,0.35)',
    accent: '#557EFF',
  },
  preasignacion: {
    bg: 'rgba(224,138,0,0.14)',
    color: '#8A5400',
    border: 'rgba(224,138,0,0.35)',
    accent: '#E08A00',
  },
  asignado: {
    bg: 'rgba(99,102,241,0.14)',
    color: '#4F46E5',
    border: 'rgba(99,102,241,0.35)',
    accent: '#6366F1',
  },
  entregado: {
    bg: 'rgba(0,169,157,0.14)',
    color: '#007A71',
    border: 'rgba(0,169,157,0.35)',
    accent: '#00A99D',
  },
  aprobado: {
    bg: 'rgba(140,198,63,0.14)',
    color: '#557926',
    border: 'rgba(140,198,63,0.35)',
    accent: '#8CC63F',
  },
  rechazado: {
    bg: 'rgba(255,0,0,0.14)',
    color: '#CC0000',
    border: 'rgba(255,0,0,0.35)',
    accent: '#FF0000',
  },
  anulado: {
    bg: 'rgba(193,39,45,0.14)',
    color: '#C1272D',
    border: 'rgba(193,39,45,0.35)',
    accent: '#C1272D',
  },
  revocado: {
    bg: 'rgba(139,92,246,0.14)',
    color: '#6D28D9',
    border: 'rgba(139,92,246,0.35)',
    accent: '#8B5CF6',
  },
  subsanacion: {
    bg: 'rgba(255,78,0,0.14)',
    color: '#BF3B00',
    border: 'rgba(255,78,0,0.35)',
    accent: '#FF4E00',
  },
};

/**
 * Icono de cada estado: el SVG de la línea gráfica, servido desde `public/`. Trae el círculo de
 * color dentro (el mismo `accent` de arriba), así que se pinta tal cual — sin pastilla tintada
 * alrededor y sin recolorear por CSS.
 */
export const ESTADO_ICONO: Record<EstadoTramite, string> = {
  borrador: '/assets/estados/borrador.svg',
  anulado: '/assets/estados/anulado.svg',
  preparado: '/assets/estados/preparado.svg',
  preasignacion: '/assets/estados/preasignacion.svg',
  asignado: '/assets/estados/asignado.svg',
  entregado: '/assets/estados/entregado.svg',
  aprobado: '/assets/estados/aprobado.svg',
  rechazado: '/assets/estados/rechazado.svg',
  revocado: '/assets/estados/revocado.svg',
  subsanacion: '/assets/estados/subsanacion.svg',
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

/** ¿El rechazo vino de la cola de placa (ADR-0059)? */
export function esRechazadoDesdePreasignacion(
  status: string | null | undefined,
  rejectedFrom: string | null | undefined,
): boolean {
  return status === 'rechazado' && rejectedFrom === REJECTED_FROM_PREASIGNACION;
}

/**
 * Label del estado con el distintivo de origen del rechazo: «Rechazado preasignación» cuando el OT
 * rechazó desde la cola de placa; en cualquier otro caso, {@link estadoLabel}.
 */
export function estadoLabelConOrigen(
  status: string | null | undefined,
  rejectedFrom: string | null | undefined,
): string {
  return esRechazadoDesdePreasignacion(status, rejectedFrom)
    ? RECHAZADO_PREASIGNACION_LABEL
    : estadoLabel(status);
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
