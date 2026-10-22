/**
 * Catálogo canónico de copy OT ↔ Gestor (épica #12551, HU #12693, Feature #12689).
 *
 * Fuente de verdad de producto: docs/ado-drafts/epic-12551/inventario-glosario.md (columna Ganador).
 * Una fila N/A o sin Ganador no se publica aquí: no hay sinónimo inventado.
 *
 * Estados de trámite (B01–B14) NO viven en este objeto: se leen de `estados.ts`
 * (`estadoLabel` / `ESTADO_LABELS` / `revocationRequestLabel`).
 */

import {
  ESTADO_LABELS,
  estadoLabel,
  revocationRequestLabel,
  type EstadoTramite,
} from '@/lib/tramites/estados';

/** Filas del Bloque A cuyo Ganador es N/A: no son texto de UI homologable. */
export const COPY_NA_KEYS = [
  'A11',
  'A14',
  'A15',
  'A16',
  'A18',
  'A19',
  'A20',
  'A21',
] as const;

export type CopyNaKey = (typeof COPY_NA_KEYS)[number];

/**
 * Textos publicados. OT y gestor leen el mismo símbolo (`copyLabel` / `copyForOtAndGestor`).
 * Claves compuestas (A02, A09, B21) se parten porque el layout RN-07 no unifica columnas.
 */
export const COPY = {
  A01: 'Vendedor',
  A02Vin: 'VIN',
  A02Placa: 'Placa',
  A03: 'Trámite',
  A04: 'Fecha radicación',
  A05: 'Organismo de tránsito',
  A06: 'Gestor',
  A07: 'Actores del trámite',
  A08: 'Especificaciones técnicas',
  A09Motor: 'Nº Motor',
  A09Chasis: 'Nº Chasis',
  A09Serie: 'Nº Serie',
  A10: 'Capacidad',
  A12: 'Ver consolidado',
  A13: 'Exportar',
  A17: 'Identidad',
  A22: 'Ahora mismo',
  A23: 'Usuarios',
  A24: 'SOAT',
  B15: 'Radicado',
  B17: 'Comprador',
  B18: 'Estado',
  B19: 'Ver documentos',
  B20: 'Invitar usuario',
  B21Tramites: 'Trámites',
  B21Reportes: 'Reportes',
  B21Usuarios: 'Usuarios',
  B21Ayuda: 'Ayuda',
  B22: 'Centro de Ayuda',
  B23Firmado: 'Firmado',
  B23SinFirma: 'Sin firma',
  B23Rechazado: 'Rechazado',
  B23SinRegistrar: 'Sin registrar',
  E01Aprobar: 'Aprobar',
  E01Rechazar: 'Rechazar',
  E02: 'Adjuntar LT',
  E03: 'Total trámites',
  E05: 'Consolidado generado.',
} as const;

export type CopyKey = keyof typeof COPY;

const COPY_NA_SET: ReadonlySet<string> = new Set(COPY_NA_KEYS);

export function isPublishedCopyKey(key: string): key is CopyKey {
  return Object.prototype.hasOwnProperty.call(COPY, key);
}

export function isNaCopyKey(key: string): key is CopyNaKey {
  return COPY_NA_SET.has(key);
}

/**
 * Texto canónico para un símbolo publicado.
 * Claves N/A o desconocidas no devuelven un sinónimo: retornan `undefined`.
 */
export function copyLabel(key: string): string | undefined {
  if (isNaCopyKey(key) || !isPublishedCopyKey(key)) return undefined;
  return COPY[key];
}

/** Misma palabra en ambas superficies: el símbolo no se biforca por rol. */
export function copyForOtAndGestor(key: CopyKey): { ot: string; gestor: string } {
  const text = COPY[key];
  return { ot: text, gestor: text };
}

/** B01–B10: alias del catálogo de negocio; no duplica Borrador/Aprobado/etc. */
export const copyEstado = estadoLabel;

export { ESTADO_LABELS, estadoLabel, revocationRequestLabel };
export type { EstadoTramite };
