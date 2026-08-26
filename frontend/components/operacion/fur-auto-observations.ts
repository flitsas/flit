import type { FieldValue } from '@/lib/api/types/procedure-runtime';
import {
  BLINDAJE_NIVEL_FIELD_KEY,
  blindajeObservacionFur,
  parseBlindajeOpcion,
} from '@/lib/catalogs/blindaje';
import {
  CANCELACION_CAUSAL_FIELD_KEY,
  cancelacionObservacionFur,
  parseCancelacionCausal,
} from '@/lib/catalogs/cancelacion';

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

  // Causal de la cancelación de matrícula. Va primero, como en el backend: es el bloque del TIPO, y
  // el campo solo lo escribe la tarjeta de CANCELACION_MATRICULA, así que su presencia ya significa
  // que el trámite es ese.
  const cancelacion = cancelacionObservacionFur(
    parseCancelacionCausal(valueOf(fields, CANCELACION_CAUSAL_FIELD_KEY)),
  );
  if (cancelacion) segments.push(cancelacion);

  // ADR-0029 — transformaciones declaradas: solo se imprime el valor NUEVO, porque los campos del
  // vehículo en el FUR conservan el dato original del RUNT.
  const cambios: [boolean, string][] = [];
  if (hasChanged(valueOf(fields, 'vehicle_color_runt'), valueOf(fields, 'vehicle_color'))
    || valueOf(fields, 'cambio_color').toLowerCase() === 'true') {
    cambios.push([true, `Color nuevo(NUEVO COLOR: ${display(valueOf(fields, 'vehicle_color'))})`]);
  }
  if (hasChanged(valueOf(fields, 'vehicle_body_type_runt'), valueOf(fields, 'vehicle_body_type'))
    || valueOf(fields, 'cambio_carroceria').toLowerCase() === 'true') {
    cambios.push([true, `Carroceria nueva(NUEVA CARROCERIA: ${display(valueOf(fields, 'vehicle_body_type'))})`]);
  }
  if (hasChanged(valueOf(fields, 'vehicle_fuel_runt'), valueOf(fields, 'vehicle_fuel'))
    || valueOf(fields, 'cambio_combustible').toLowerCase() === 'true') {
    cambios.push([true, `COMBUSTIBLE_NUEVO: ${display(valueOf(fields, 'vehicle_fuel'))}`]);
  }
  for (const [, texto] of cambios) segments.push(texto);

  // Blindaje: el nivel (o el desmonte) no tiene casilla donde declararse —la del formulario es un
  // SÍ/NO— así que el detalle vive aquí. `blindaje_nivel` solo lo escribe la tarjeta del tipo
  // BLINDAJE, de modo que su presencia ya significa que el trámite lo lleva.
  const blindaje = blindajeObservacionFur(
    parseBlindajeOpcion(valueOf(fields, BLINDAJE_NIVEL_FIELD_KEY)),
  );
  if (blindaje) segments.push(blindaje);

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
