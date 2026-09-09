/**
 * Cliente tipado del módulo "Generación documental" (HU-01, Feature #12201).
 *
 * Toda llamada pasa por `apiFetch` (JWT + normalización de errores). No hay `fetch` suelto
 * en los componentes. La generación NO devuelve el PDF: responde `{ id, status }` y la
 * descarga se resuelve después con `requestStandaloneDocumentDownload`.
 */
import { apiFetch } from "./client";
import type {
  StandaloneDocumentDownloadLink,
  StandaloneDocumentGenerateResult,
  StandaloneDocumentsListParams,
  StandaloneDocumentsPagedResult,
  StandaloneRuesPreviewResult,
  TransferGenerateRequest,
  TransferGenerateResult,
} from "./types-generacion-documental";

export const GENERACION_DOCUMENTAL_API_BASE = "/api/v1/admin/generacion-documental";

/** `GET /api/v1/admin/generacion-documental` — historial paginado del tenant (CF-17/CF-18). */
export function fetchStandaloneDocuments(
  params: StandaloneDocumentsListParams = {},
  signal?: AbortSignal,
): Promise<StandaloneDocumentsPagedResult> {
  const { status, ...rest } = params;
  return apiFetch<StandaloneDocumentsPagedResult>(GENERACION_DOCUMENTAL_API_BASE, {
    // `status` se repite una vez por estado interno (pending+processing = «En proceso»).
    query: { ...rest, status },
    signal,
  });
}

/** `GET /{id}/download` — presigned URL + auditoría de descarga (CF-19). */
export function requestStandaloneDocumentDownload(
  id: string,
  signal?: AbortSignal,
): Promise<StandaloneDocumentDownloadLink> {
  return apiFetch<StandaloneDocumentDownloadLink>(`${GENERACION_DOCUMENTAL_API_BASE}/${id}/download`, {
    signal,
  });
}

/** `POST /rues/preview` — revisión previa en vivo; no persiste nada (CF-04). */
export function previewRuesCompany(
  nit: string,
  signal?: AbortSignal,
): Promise<StandaloneRuesPreviewResult> {
  return apiFetch<StandaloneRuesPreviewResult>(`${GENERACION_DOCUMENTAL_API_BASE}/rues/preview`, {
    method: "POST",
    body: { nit },
    signal,
  });
}

/**
 * `POST /rues/generate` — genera el Certificado RUES standalone.
 *
 * Devuelve `{ id, status }` en `application/json`; jamás `application/pdf`.
 * La cabecera `Idempotency-Key` (CF-16) la añadirá HU-02 junto con el soporte de headers
 * en `apiFetch`, que hoy no los admite.
 */
export function generateRuesDocument(
  nit: string,
  signal?: AbortSignal,
): Promise<StandaloneDocumentGenerateResult> {
  return apiFetch<StandaloneDocumentGenerateResult>(`${GENERACION_DOCUMENTAL_API_BASE}/rues/generate`, {
    method: "POST",
    body: { nit },
    signal,
  });
}

/**
 * `POST /transferencia/generate` — emite el Documento de Transferencia de Dominio (escenario A).
 *
 * Devuelve `{ id, status, advisories }` en `application/json`; jamás `application/pdf`. Un 422
 * llega como `ApiValidationError` con `errors[]`, donde cada elemento trae `code`, `field` y
 * `message` del anexo normativo y **nunca** el valor capturado.
 */
export function generateTransferenciaDocument(
  request: TransferGenerateRequest,
  signal?: AbortSignal,
): Promise<TransferGenerateResult> {
  return apiFetch<TransferGenerateResult>(
    `${GENERACION_DOCUMENTAL_API_BASE}/transferencia/generate`,
    { method: "POST", body: request, signal },
  );
}
