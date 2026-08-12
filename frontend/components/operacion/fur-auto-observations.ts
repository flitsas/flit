import type { FieldValue } from '@/lib/api/types/procedure-runtime';

/**
 * Texto que el backend AÑADE por su cuenta al recuadro de observaciones del FUR, para poder
 * mostrárselo al gestor en el paso donde escribe las suyas.
 *
 * <p>Hasta ahora esos textos solo aparecían al generar el FUR: el gestor declaraba una
 * transformación o elegía un tipo de servicio y no volvía a ver ese dato hasta tener el PDF
 * delante. Aquí se previsualizan, en solo lectura — el textarea sigue conteniendo únicamente lo
 * que el gestor escribe, así que nada se duplica ni se puede borrar por accidente.</p>
 *
 * <p><b>Espejo deliberado.</b> Las reglas viven en el backend, que es quien imprime el FUR:
 * `FurTransformationObservations.Compose` y `FurServicioVinculadoraObservation.Compose`. Esto es
 * una copia para la vista previa; si cambia una redacción allí, cambia aquí. Los tests de ambos
 * lados usan los mismos ejemplos para que la deriva se note.</p>
 */

const SERVICIO_LEGIBLE: Record<string, string> = {
  PARTICULAR: 'PARTICULAR',
  PUBLICO: 'PÚBLICO',
  DIPLOMATICO: 'DIPLOMÁTICO',
  OFICIAL: 'OFICIAL',
  ESPECIAL: 'ESPECIAL',
  OTROS: 'OTROS',
};

function valueOf(fields: FieldValue[], key: string): string {
  return fields.find((f) => f.fieldKey === key)?.valueText?.trim() ?? '';
}

/** Hay cambio declarado si el snapshot RUNT existe y el valor efectivo difiere (trim + case-insensitive). */
function hasChanged(runt: string, efectivo: string): boolean {
  return Boolean(runt) && Boolean(efectivo) && runt.toUpperCase() !== efectivo.toUpperCase();
}

function display(value: string): string {
  return value ? value.toUpperCase() : '-';
}

/**
 * Segmentos que se anexarán al FUR, en el mismo orden en que los imprime el backend.
 * Lista vacía = el recuadro llevará solo lo que escriba el gestor.
 */
export function furAutoObservations(fields: FieldValue[] | null | undefined): string[] {
  if (!fields?.length) return [];

  const segments: string[] = [];

  // ADR-0029 — transformaciones declaradas: solo se imprime el valor NUEVO, porque los campos del
  // vehículo en el FUR conservan el dato original del RUNT.
  const cambios: [string, string, string][] = [
    ['color', 'vehicle_color_runt', 'vehicle_color'],
    ['combustible', 'vehicle_fuel_runt', 'vehicle_fuel'],
    ['carrocería', 'vehicle_body_type_runt', 'vehicle_body_type'],
  ];
  for (const [etiqueta, runtKey, efectivoKey] of cambios) {
    const runt = valueOf(fields, runtKey);
    const efectivo = valueOf(fields, efectivoKey);
    if (hasChanged(runt, efectivo)) segments.push(`Cambio de ${etiqueta}: ${display(efectivo)}.`);
  }

  // Tipo de servicio + empresa vinculadora. Sin razón social no se imprime nada: el tipo de
  // servicio ya tiene su casilla propia y repetirlo solo gastaría renglones del recuadro.
  const razonSocial = valueOf(fields, 'empresa_vinculadora_razon_social');
  if (razonSocial) {
    const nit = valueOf(fields, 'empresa_vinculadora_nit');
    const empresa = nit ? `${razonSocial}, NIT ${nit}` : razonSocial;
    const code = valueOf(fields, 'vehicle_service');
    const servicio = code ? (SERVICIO_LEGIBLE[code.toUpperCase()] ?? code.toUpperCase()) : '';
    segments.push(
      servicio
        ? `Servicio: ${servicio}. Empresa vinculadora: ${empresa}.`
        : `Empresa vinculadora: ${empresa}.`,
    );
  }

  return segments;
}
