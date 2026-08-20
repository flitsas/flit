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

/**
 * El recuadro de observaciones tal como quedará: lo que escribe el gestor primero y el texto
 * automático detrás, en el MISMO orden en que los une el backend
 * (`FurTransformationObservations.Compose` antepone las manuales; el bloque de servicio/vinculadora
 * va al final).
 *
 * <p><b>Lo que esta vista previa no incluye:</b> el bloque de gravamen (`FurPrendaObservation`), que
 * el FUR antepone a todo cuando hay prenda vigente con acreedor. Ese dato no viaja en
 * `field_values` —sale del agregado de prenda— y traerlo aquí exigiría otra llamada y una tercera
 * copia de la regla de marcado. Por eso el encabezado habla de las observaciones, no del recuadro
 * entero.</p>
 */
export function furObservationsPreview(
  manual: string | null | undefined,
  fields: FieldValue[] | null | undefined,
): { manual: string | null; auto: string[] } {
  const escrito = manual?.trim();
  return { manual: escrito ? escrito : null, auto: furAutoObservations(fields) };
}

/**
 * HU #11643 — presupuesto de caracteres del recuadro OBSERVACIONES del FUR.
 *
 * <p>Espejo de `FurObservacionesComposer.PresupuestoCaracteres` (backend), medido allí con la fuente
 * real sobre la geometría del manifiesto. Se replica aquí para poder avisar al gestor MIENTRAS
 * escribe, en vez de que descubra el recorte con el PDF ya generado.</p>
 *
 * <p>Lo automático tiene prioridad y entra íntegro, así que lo que le queda al texto libre depende
 * de cuánto ocupe aquello: declarar una transformación reduce el espacio disponible, y el contador
 * lo refleja en vivo.</p>
 */
export const FUR_OBSERVACIONES_PRESUPUESTO = 500;

/** Caracteres que le quedan al texto libre una vez reservado el bloque automático. */
export function furObservacionesDisponibles(auto: string[]): number {
  const autoLen = auto.join(' ').trim().length;
  if (autoLen === 0) return FUR_OBSERVACIONES_PRESUPUESTO;
  return Math.max(0, FUR_OBSERVACIONES_PRESUPUESTO - autoLen - 1);
}
