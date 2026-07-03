// Cliente tipado del módulo "Generación de improntas" (HU #10469/#10471 frontend,
// Feature #10462). El endpoint POST /api/v1/admin/improntas/generate está documentado
// en el diseño técnico del Feature (integración Kyverum RUNT); el backend real se
// implementa en la HU #10467. Se consume siguiendo el patrón de descarga binaria de
// `exportExecutivePdf` (lib/api/analytics.ts), vía el helper `downloadFile`.
import { downloadFile } from "./download";
import { ApiError } from "./types";
import type { GenerarImprontaRequest, GenerarImprontaResult, ImprontaErrorBody } from "./types-improntas";

const base = "/api/v1/admin/improntas";

/**
 * Headers de respuesta con la metadata de trazabilidad (radicado/hash) de una
 * generación exitosa — ver limitación documentada en `GenerarImprontaResult`.
 */
const RADICADO_HEADER = "X-Impronta-Radicado";
const HASH_HEADER = "X-Impronta-Hash";

/**
 * POST /generate — genera el Certificado de Improntas Digitales del vehículo vía
 * Kyverum RUNT y descarga el PDF resultante al equipo del usuario (HU #10469/#10471
 * AC1). Lanza `ApiError` con el status del backend ante 4xx/5xx — usa
 * `describeImprontaError` para traducirlo a un mensaje específico por tipo de error
 * (AC2). Devuelve el radicado/hash si el backend los expuso en headers de respuesta.
 */
export async function generarImpronta(
  body: GenerarImprontaRequest,
  signal?: AbortSignal,
): Promise<GenerarImprontaResult> {
  const headers = await downloadFile(`${base}/generate`, {
    method: "POST",
    body,
    fallbackFilename: `impronta_${body.placa.trim().toUpperCase() || "vehiculo"}.pdf`,
    signal,
    captureHeaders: [RADICADO_HEADER, HASH_HEADER],
  });

  return {
    radicado: headers[RADICADO_HEADER] ?? null,
    hash: headers[HASH_HEADER] ?? null,
  };
}

/**
 * Traduce el error de `generarImpronta` a un mensaje específico por tipo (HU #10471,
 * AC2), sin exponer detalles técnicos crudos del proveedor externo (Kyverum RUNT):
 * - 422 `VALIDATION_ERROR`: datos del vehículo inválidos; incluye el primer detalle de
 *   campo si el backend lo entrega (`errors[]`, mismo shape que `ValidationErrorResponse`).
 * - 401 `UNAUTHORIZED`: key/scope inválido en el proveedor — mensaje genérico de "no
 *   autorizado", nunca el detalle crudo de Kyverum.
 * - 502 `UPSTREAM_UNAVAILABLE`: servicio de Kyverum RUNT saturado/caído — reintentable.
 * - Cualquier otro status o error de red/desconocido: mensaje genérico de fallback.
 */
export function describeImprontaError(error: unknown): string {
  if (error instanceof ApiError) {
    const body = (error.body ?? null) as ImprontaErrorBody | null;

    switch (error.status) {
      case 422: {
        const detail = body?.errors?.[0]?.message;
        return detail
          ? `Los datos del vehículo no son válidos: ${detail}`
          : "Los datos del vehículo no son válidos. Verifica placa, motor/chasis/serie e intenta nuevamente.";
      }
      case 401:
        return "No autorizado para generar la impronta. Verifica tus permisos o vuelve a iniciar sesión e intenta nuevamente.";
      case 502:
        return "El servicio de improntas no está disponible en este momento. Es un problema temporal del proveedor externo: intenta de nuevo en unos minutos.";
      default:
        return "No se pudo generar la impronta. Intenta nuevamente en unos minutos.";
    }
  }

  return "No se pudo conectar con el servicio de improntas. Verifica tu conexión e intenta nuevamente.";
}
