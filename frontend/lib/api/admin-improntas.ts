// Cliente tipado del módulo "Generación de improntas" (HU #10469 frontend, Feature #10462).
// El endpoint POST /api/v1/admin/improntas/generate está documentado en el diseño técnico
// del Feature (integración Kyverum RUNT); el backend real se implementa en la HU #10467,
// aún no disponible al momento de esta HU. Se consume igual, siguiendo el patrón de
// descarga binaria de `exportExecutivePdf` (lib/api/analytics.ts).
import { downloadFile } from "./download";
import type { GenerarImprontaRequest } from "./types-improntas";

const base = "/api/v1/admin/improntas";

/**
 * POST /generate — genera el Certificado de Improntas Digitales del vehículo vía
 * Kyverum RUNT y descarga el PDF resultante al equipo del usuario. Lanza `ApiError`
 * con el status del backend ante 4xx/5xx; el mapeo fino de mensajes por tipo de error
 * (validación/autenticación/servicio no disponible) es alcance de la HU #10471.
 */
export function generarImpronta(body: GenerarImprontaRequest, signal?: AbortSignal): Promise<void> {
  return downloadFile(`${base}/generate`, {
    method: "POST",
    body,
    fallbackFilename: `impronta_${body.placa.trim().toUpperCase() || "vehiculo"}.pdf`,
    signal,
  });
}
