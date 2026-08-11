// Cliente SuperAdmin — Plataforma → Notificaciones → Banco de pruebas (HU #11370, Feature #11349).
// Solo lectura: catálogo de plantillas + canales con remitente resuelto. El listado de compañías
// se reutiliza de `admin-companies.ts` (mismo contrato que el resto de Plataforma).
import { apiFetch } from "./client";

const plantillasBase = "/api/v1/admin/plataforma/notificaciones/plantillas";
const canalesBase = "/api/v1/admin/plataforma/notificaciones/canales";

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
