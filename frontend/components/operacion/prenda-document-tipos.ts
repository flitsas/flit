import type { PrendaDecision } from '@/lib/api/types/procedure-runtime';

/** Tipos de adjunto de prenda (alineados con `PrendaDocTipos` del backend). */
export const PRENDA_DOC_TIPOS = {
  solicitud: 'prenda_solicitud',
  registro: 'prenda_registro',
  levantamiento: 'prenda_levantamiento',
} as const;

/** Códigos del checklist/catálogo que se gestionan en la sección Prenda (no en Documentos). */
export const PRENDA_MANAGED_CHECKLIST_TIPOS: ReadonlySet<string> = new Set([
  'inscripcion_prenda',
  PRENDA_DOC_TIPOS.solicitud,
  PRENDA_DOC_TIPOS.registro,
  PRENDA_DOC_TIPOS.levantamiento,
]);

/** DocTipo exigido por la decisión (null si no requiere documento). */
export function prendaDocTipoFor(
  decision: PrendaDecision | '' | null | undefined,
): string | null {
  switch (decision) {
    case 'solicitar':
      return PRENDA_DOC_TIPOS.solicitud;
    case 'registrar':
      return PRENDA_DOC_TIPOS.registro;
    case 'levantar':
      return PRENDA_DOC_TIPOS.levantamiento;
    default:
      return null;
  }
}

/** Etiqueta del contenedor de carga según la decisión. */
export function prendaDocLabelFor(decision: PrendaDecision): string {
  switch (decision) {
    case 'solicitar':
      return 'Solicitud de constitución de prenda';
    case 'registrar':
      return 'Certificado / registro de prenda';
    case 'levantar':
      return 'Documento de levantamiento de gravamen';
    default:
      return 'Documento de prenda';
  }
}

export function isPrendaManagedChecklistTipo(tipo: string): boolean {
  return PRENDA_MANAGED_CHECKLIST_TIPOS.has(tipo);
}
