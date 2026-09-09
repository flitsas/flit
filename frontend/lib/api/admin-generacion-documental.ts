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
  PrefillPersonaJuridicaRequest,
  PrefillPersonaNaturalRequest,
  PrefillResult,
  PrefillVehiculoRequest,
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

// ── Prellenado standalone (CF-25) ───────────────────────────────────────────────────────────────
//
// Los tres endpoints devuelven `200 { found, source, fields[] }` incluso sin coincidencia
// (`found: false`), así que "no hay antecedente" NO llega como excepción: llega como respuesta.
// Solo un fallo real de la fuente (502 `provider_unavailable`) lanza `ApiError`, y ni siquiera eso
// impide generar el documento: el formulario queda utilizable con todos los campos editables.

/** `POST /prefill/vehiculo` — RUNT por placa, sin los gates del wizard de trámites. */
export function prefillVehiculo(
  request: PrefillVehiculoRequest,
  signal?: AbortSignal,
): Promise<PrefillResult> {
  return apiFetch<PrefillResult>(`${GENERACION_DOCUMENTAL_API_BASE}/prefill/vehiculo`, {
    method: "POST",
    body: request,
    signal,
  });
}

/**
 * `POST /prefill/persona-juridica` — cadena: directorio de representantes legales y, solo si el
 * directorio no responde, RUES.
 *
 * <b>El RUES no devuelve al representante legal.</b> Lo que certifica es la *facultad* de
 * representación, no la persona: cuando la fuente efectiva es RUES llegan razón social, domicilio y
 * matrícula mercantil, y el representante legal y su documento quedan de captura manual.
 */
export function prefillPersonaJuridica(
  request: PrefillPersonaJuridicaRequest,
  signal?: AbortSignal,
): Promise<PrefillResult> {
  return apiFetch<PrefillResult>(`${GENERACION_DOCUMENTAL_API_BASE}/prefill/persona-juridica`, {
    method: "POST",
    body: request,
    signal,
  });
}

/**
 * `POST /prefill/persona-natural` — cadena: RUNT persona y, si el RUNT no responde,
 * `contact-lookup`.
 *
 * <b>`contact-lookup` nunca devuelve nombre ni documento</b> por contrato: si la fuente efectiva es
 * esa, el nombre queda manual.
 */
export function prefillPersonaNatural(
  request: PrefillPersonaNaturalRequest,
  signal?: AbortSignal,
): Promise<PrefillResult> {
  return apiFetch<PrefillResult>(`${GENERACION_DOCUMENTAL_API_BASE}/prefill/persona-natural`, {
    method: "POST",
    body: request,
    signal,
  });
}
