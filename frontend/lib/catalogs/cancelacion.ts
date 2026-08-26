/**
 * Causales del trámite de cancelación de matrícula. Espejo de `CancelacionCausales` (dominio
 * backend): los códigos son el contrato que viaja en `field_values`, y de ellos cuelgan tanto los
 * documentos que el checklist exige como el texto que el FUR imprime en observaciones. Los dos
 * lados tienen que decir exactamente lo mismo.
 *
 * <p>Vive aparte del componente para que la vista previa de observaciones del FUR pueda leer la
 * regla sin arrastrar React ni el `use client` de la tarjeta — igual que `blindaje.ts`.</p>
 */

/** Tipo de trámite al que pertenece la causal. Espejo de `CancelacionCausales.TipoCodigo`. */
export const CANCELACION_TIPO_CODIGO = 'CANCELACION_MATRICULA';

/** Clave de `field_values` donde vive la causal. Espejo de `CancelacionCausales.FieldKey`. */
export const CANCELACION_CAUSAL_FIELD_KEY = 'cancelacion_causal';

export type CancelacionCausal =
  | 'DECISION_JUDICIAL'
  | 'PERDIDA_TOTAL_FUERZA_MAYOR'
  | 'PERDIDA_TOTAL_ACCIDENTE'
  | 'DECISION_VOLUNTARIA';

/** Códigos de `document_types` que acreditan las causales. Espejo de las constantes del dominio. */
export const CANCELACION_DOC_ACTO_JUDICIAL = 'oficio_judicial';
export const CANCELACION_DOC_DIJIN = 'certificado_dijin';
export const CANCELACION_DOC_ASEGURADORA = 'certificado_aseguradora_perito';
export const CANCELACION_DOC_AUTORIDAD = 'certificado_autoridad_administrativa';

const ETIQUETA_DOC: Record<string, string> = {
  [CANCELACION_DOC_ACTO_JUDICIAL]: 'Acto de decisión judicial',
  [CANCELACION_DOC_DIJIN]: 'Certificado DIJIN o Policía',
  [CANCELACION_DOC_ASEGURADORA]: 'Certificado de aseguradora o perito',
  [CANCELACION_DOC_AUTORIDAD]: 'Certificado de autoridad administrativa',
};

/**
 * Causales en el mismo orden en que las declara el dominio (`CancelacionCausales.Codigos`), con los
 * documentos que cada una exige. Son TODOS obligatorios, no uno cualquiera de la lista.
 */
export const CANCELACION_CAUSALES: {
  codigo: CancelacionCausal;
  label: string;
  documentos: string[];
}[] = [
  {
    codigo: 'DECISION_JUDICIAL',
    label: 'Decisión judicial',
    documentos: [CANCELACION_DOC_ACTO_JUDICIAL],
  },
  {
    codigo: 'PERDIDA_TOTAL_FUERZA_MAYOR',
    label: 'Pérdida total por fuerza mayor',
    documentos: [CANCELACION_DOC_DIJIN, CANCELACION_DOC_ASEGURADORA, CANCELACION_DOC_AUTORIDAD],
  },
  {
    codigo: 'PERDIDA_TOTAL_ACCIDENTE',
    label: 'Pérdida total por accidente',
    documentos: [CANCELACION_DOC_DIJIN, CANCELACION_DOC_ASEGURADORA, CANCELACION_DOC_AUTORIDAD],
  },
  {
    codigo: 'DECISION_VOLUNTARIA',
    label: 'Decisión voluntaria',
    documentos: [CANCELACION_DOC_DIJIN],
  },
];

const POR_CODIGO = new Map(CANCELACION_CAUSALES.map((c) => [c.codigo as string, c]));

/** ¿El tipo de trámite es el que declara causal? */
export function esCancelacionDeMatricula(tipoCodigo: string | null | undefined): boolean {
  return (tipoCodigo ?? '').trim().toUpperCase() === CANCELACION_TIPO_CODIGO;
}

/** Lee la causal persistida; cualquier valor no reconocido se trata como «sin declarar». */
export function parseCancelacionCausal(
  valor: string | null | undefined,
): CancelacionCausal | null {
  const v = (valor ?? '').trim().toUpperCase();
  return POR_CODIGO.has(v) ? (v as CancelacionCausal) : null;
}

/** Documentos obligatorios de la causal (todos). Sin causal declarada: ninguno. */
export function documentosDeCausal(causal: CancelacionCausal | null): string[] {
  return causal ? (POR_CODIGO.get(causal)?.documentos ?? []) : [];
}

/** Rótulo del documento, el mismo que usa el checklist. */
export function etiquetaDocumento(docTipo: string): string {
  return ETIQUETA_DOC[docTipo] ?? docTipo;
}

/**
 * Texto que el FUR anexará a las observaciones. Espejo de
 * `FurTramiteObservation.ComposeCancelacion`: si cambia la redacción allí, cambia aquí, y los tests
 * de ambos lados usan los mismos ejemplos.
 */
export function cancelacionObservacionFur(causal: CancelacionCausal | null): string | null {
  switch (causal) {
    case 'DECISION_JUDICIAL':
      return 'CANCELACIÓN POR DECISIÓN JUDICIAL.';
    case 'PERDIDA_TOTAL_FUERZA_MAYOR':
      return 'CANCELACIÓN POR PÉRDIDA TOTAL - FUERZA MAYOR.';
    case 'PERDIDA_TOTAL_ACCIDENTE':
      return 'CANCELACIÓN POR PÉRDIDA TOTAL - ACCIDENTE.';
    case 'DECISION_VOLUNTARIA':
      return 'CANCELACIÓN POR DECISIÓN VOLUNTARIA.';
    default:
      return null;
  }
}
