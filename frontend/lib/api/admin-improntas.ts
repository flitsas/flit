// Cliente tipado del módulo "Generación de improntas" (HU #10469/#10470/#10471 frontend,
// Feature #10462).
import { apiFetch } from "./client";
import { downloadFile } from "./download";
import { ApiError } from "./types";
import type {
  GenerarImprontaRequest,
  GenerarImprontaResult,
  ImprontaErrorBody,
  ImprontasHistorialPagedResult,
  ImprontasHistorialParams,
} from "./types-improntas";

const base = "/api/v1/admin/improntas";

const RADICADO_HEADER = "X-Impronta-Radicado";
const HASH_HEADER = "X-Impronta-Hash";

/**
 * POST /generate — genera el Certificado de Improntas Digitales y descarga el PDF.
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
 * Traduce el error de `generarImpronta` a un mensaje específico por tipo (HU #10471 AC2).
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

/**
 * GET /api/v1/admin/improntas — historial paginado/filtrable (HU #10470).
 */
export function fetchImprontasHistorial(
  params: ImprontasHistorialParams = {},
  signal?: AbortSignal,
): Promise<ImprontasHistorialPagedResult> {
  return apiFetch<ImprontasHistorialPagedResult>(base, {
    query: { ...params },
    signal,
  });
}
