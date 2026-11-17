/**
 * Soporte del impuesto departamental con el trámite en `asignado`, dentro del modal «Enviar al OT»:
 * mismo lugar y misma caja que el PDF del SOAT (Bug #13194). El backend permite el tipo
 * `liquidacion_impuesto` en `asignado` (`AttachmentRules.ImpuestoEvidenceTipos`).
 *
 * A diferencia del SOAT no hay lectura del PDF ni re-verificación: el RUNT no reporta el impuesto, así
 * que el flujo es solo subir el adjunto. Si FLITO ya cargó el comprobante, el backend responde 409
 * `adjunto_bloqueado_flito` («gana quien carga primero», HU #13265) y se muestra su copy.
 *
 * Devuelve un resultado con el texto para la UI; nunca lanza.
 */
import { mensajeErrorAdjunto } from '@/lib/tramites/flito';

/** Código del catálogo de documentos (el mismo que carga FLITO). */
export const TIPO_SOPORTE_IMPUESTO = 'liquidacion_impuesto';

/** Subconjunto de `tramitesClient` que usa el flujo (inyectable en pruebas). */
export interface SoporteImpuestoClient {
  uploadAttachment: (instanceId: string, tipo: string, file: File, tenantId?: string) => Promise<unknown>;
}

export type EstadoSoporteImpuesto = 'registrado' | 'error';

export interface ResultadoSoporteImpuesto {
  estado: EstadoSoporteImpuesto;
  mensaje: string;
}

export const COPY_SOPORTE_IMPUESTO_REGISTRADO =
  'Se cargó el soporte del impuesto departamental en el trámite. Ya puedes enviarlo al OT.';

export const COPY_SOPORTE_IMPUESTO_ERROR =
  'No se pudo cargar el PDF del impuesto departamental. Inténtalo de nuevo.';

export async function cargarSoporteImpuestoAsignado(
  client: SoporteImpuestoClient,
  instanceId: string,
  file: File,
  tenantId?: string,
): Promise<ResultadoSoporteImpuesto> {
  try {
    await client.uploadAttachment(instanceId, TIPO_SOPORTE_IMPUESTO, file, tenantId);
    return { estado: 'registrado', mensaje: COPY_SOPORTE_IMPUESTO_REGISTRADO };
  } catch (err) {
    return { estado: 'error', mensaje: mensajeErrorAdjunto(err, COPY_SOPORTE_IMPUESTO_ERROR) };
  }
}
