// Cliente SuperAdmin — Plataforma → Notificaciones → Banco de pruebas (HU #11370/#11371,
// Feature #11349). HU #11370: catálogo de plantillas + canales con remitente resuelto (solo
// lectura). HU #11371: render de muestra aislado, buzón de pruebas (leer/actualizar) y envío de
// prueba — ver `services/core-api/src/Flit.Api/Endpoints/AdminPlataformaNotificacionesEndpoints.cs`
// para los contratos exactos (fuente de verdad, no modificar desde el frontend). El listado de
// compañías se reutiliza de `admin-companies.ts` (mismo contrato que el resto de Plataforma).
import { apiFetch } from "./client";

const plantillasBase = "/api/v1/admin/plataforma/notificaciones/plantillas";
const canalesBase = "/api/v1/admin/plataforma/notificaciones/canales";
const buzonPruebasBase = "/api/v1/admin/plataforma/notificaciones/buzon-pruebas";

/** Entrada del catálogo de plantillas (AC1 de la HU #11370, contrato de la HU #11356). */
export interface NotificationTemplateItem {
  id: string;
  name: string;
  /** `"Security"` o `"Analytics"` según el catálogo del backend. */
  module: string;
  triggers: string[];
}

/** Canal de notificación con su remitente resuelto por configuración (HU #11367). */
export interface NotificationChannelItem {
  channel: string;
  label: string;
  isDefault: boolean;
  isConfigured: boolean;
  senderEmail: string | null;
  senderName: string | null;
}

/** `GET /api/v1/admin/plataforma/notificaciones/plantillas` — catálogo íntegro (5 elementos). */
export async function listNotificationTemplates(
  signal?: AbortSignal,
): Promise<NotificationTemplateItem[]> {
  const data = await apiFetch<{ items: NotificationTemplateItem[] }>(plantillasBase, { signal });
  return data.items ?? [];
}

/** `GET /api/v1/admin/plataforma/notificaciones/canales` — canales con remitente resuelto. */
export async function listNotificationChannels(
  signal?: AbortSignal,
): Promise<NotificationChannelItem[]> {
  const data = await apiFetch<{ channels: NotificationChannelItem[] }>(canalesBase, { signal });
  return data.channels ?? [];
}

// ── HU #11371 — Ver en vivo aislado ─────────────────────────────────────────

/** Render de muestra de una plantilla (AC1/AC2). Nunca se envía correo al pedirlo. */
export interface NotificationSample {
  templateId: string;
  subject: string;
  html: string;
}

/**
 * `GET /api/v1/admin/plataforma/notificaciones/plantillas/{templateId}/muestra` — HTML de
 * muestra para renderizar en el `iframe` aislado. `404` si el id no existe en el catálogo.
 */
export async function getNotificationSample(
  templateId: string,
  signal?: AbortSignal,
): Promise<NotificationSample> {
  return apiFetch<NotificationSample>(
    `${plantillasBase}/${encodeURIComponent(templateId)}/muestra`,
    { signal },
  );
}

// ── HU #11371 — Buzón de pruebas ────────────────────────────────────────────

/** Buzón de pruebas global de plataforma (AC5/AC6). */
export interface NotificationTestMailbox {
  isConfigured: boolean;
  testRecipientEmail: string | null;
  lastTestSentAt: string | null;
  rowVersion: number;
}

/** `GET /api/v1/admin/plataforma/notificaciones/buzon-pruebas`. */
export async function getTestMailbox(signal?: AbortSignal): Promise<NotificationTestMailbox> {
  return apiFetch<NotificationTestMailbox>(buzonPruebasBase, { signal });
}

/**
 * `PUT /api/v1/admin/plataforma/notificaciones/buzon-pruebas` (AC6). `400` con
 * `{ error: "correo_invalido" }` si el correo no es válido; `409` con
 * `{ error: "row_version_conflict" }` si `rowVersion` quedó desactualizado.
 */
export async function updateTestMailbox(
  email: string,
  rowVersion: number | null,
  signal?: AbortSignal,
): Promise<NotificationTestMailbox> {
  return apiFetch<NotificationTestMailbox>(buzonPruebasBase, {
    method: "PUT",
    body: { email, rowVersion },
    signal,
  });
}

// ── HU #11371 — Envío de prueba ─────────────────────────────────────────────

export type NotificationTestChannel = "FLIT_SMTP" | "TENANT_API";

/**
 * Resultado de `POST /api/v1/admin/plataforma/notificaciones/buzon-pruebas/envios` (AC3/AC4).
 * `success: false` con `outcome: "TransportFailed"` también llega en `200` — es un resultado del
 * intento, no un error HTTP. Errores reales (400/404/429/500) los lanza `apiFetch` como
 * `ApiError` — ver `mapSendTestFailure` en `lib/notificaciones/mensajes-envio-prueba.ts`.
 */
export interface NotificationTestSendResult {
  success: boolean;
  outcome: "Sent" | "TransportFailed" | string;
  message: string;
  templateId: string | null;
  channel: string | null;
  senderEmail: string | null;
  senderName: string | null;
  sentAt: string | null;
  /** `true` = NO salió un correo real (transporte de consola de este ambiente). */
  isConsoleTransport: boolean;
  /**
   * `true` = el correo NO llegó al buzón de pruebas configurado: se desvió a una cuenta de
   * desvío del equipo. Fuera de producción ese desvío es obligatorio para el canal Renting —
   * es la única barrera contra alcanzar a un cliente final real. Nunca mostrar la dirección de
   * desvío (dato personal, Ley 1581) aunque el backend llegue a enviarla.
   */
  recipientDiverted: boolean;
}

/** `POST /api/v1/admin/plataforma/notificaciones/buzon-pruebas/envios`. */
export async function sendNotificationTest(
  templateId: string,
  channel: NotificationTestChannel,
  signal?: AbortSignal,
): Promise<NotificationTestSendResult> {
  return apiFetch<NotificationTestSendResult>(`${buzonPruebasBase}/envios`, {
    method: "POST",
    body: { templateId, channel },
    signal,
  });
}
