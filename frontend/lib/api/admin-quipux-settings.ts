// Cliente tipado de la configuración operativa GLOBAL de Quipux (admin.quipux_settings,
// HU #10710). Exclusivo SuperAdmin (el endpoint exige SuperAdminPolicy).
//
// OJO — no confundir con `admin-transit-office-tenants.ts`, que parametriza la radicación
// POR SECRETARÍA destino (código DIVIPO + banderas). Esto de aquí es la configuración de
// PLATAFORMA: las credenciales de FLIT como consumidor de Quipux, el bucket S3, y las
// cadencias de los workers. Es una fila única, sin tenant.
import { apiFetch } from "./client";

/**
 * Vista de la configuración vigente. NUNCA trae los secretos: solo indica si HAY uno cargado
 * (`hasPassword`, `hasAwsSecretAccessKey`), igual que cualquier formulario de credenciales —
 * el front sabe si existe un secreto, no cuál es.
 */
export interface QuipuxSettings {
  enabled: boolean;
  urlLogin: string;
  urlRegisterDocument: string;
  urlValidateStatus: string;
  username: string;
  hasPassword: boolean;
  consumerCode: string;
  bucket: string;
  s3Prefix: string;
  awsRegion: string;
  awsAccessKeyId: string;
  hasAwsSecretAccessKey: boolean;
  officerDocumentType: number;
  officerDocumentNumber: string;
  registerIntervalMinutes: number;
  pollIntervalMinutes: number;
  batchSize: number;
  maxAttempts: number;
  maxPolls: number;
  timeoutSeconds: number;
  /** ¿Alcanza la configuración para que los workers radiquen? La calcula el backend. */
  estaCompleta: boolean;
  updatedAt: string | null;
}

/**
 * Cuerpo del PUT. Los dos secretos son opcionales: enviarlos vacíos/nulos CONSERVA el valor
 * cifrado ya almacenado (no lo borra). Solo mándalos con valor cuando quieras cambiarlos.
 */
export interface SaveQuipuxSettingsRequest {
  enabled: boolean;
  urlLogin: string;
  urlRegisterDocument: string;
  urlValidateStatus: string;
  username: string;
  /** Vacío/nulo = conservar el existente. No se borra nunca por omisión. */
  password?: string | null;
  consumerCode: string;
  bucket: string;
  s3Prefix: string;
  awsRegion: string;
  awsAccessKeyId: string;
  /** Vacío/nulo = conservar el existente. No se borra nunca por omisión. */
  awsSecretAccessKey?: string | null;
  officerDocumentType: number;
  officerDocumentNumber: string;
  registerIntervalMinutes: number;
  pollIntervalMinutes: number;
  batchSize: number;
  maxAttempts: number;
  maxPolls: number;
  timeoutSeconds: number;
}

const base = "/api/v1/admin/quipux/settings";

/**
 * GET — configuración vigente (redactada). `null` cuando aún no hay fila (el backend responde
 * 204): es el estado inicial normal, no un error.
 */
export async function fetchQuipuxSettings(signal?: AbortSignal): Promise<QuipuxSettings | null> {
  const view = await apiFetch<QuipuxSettings | undefined>(base, { signal });
  return view ?? null;
}

/** PUT — alta o actualización de la fila única. Cifra los secretos en el backend. */
export function saveQuipuxSettings(body: SaveQuipuxSettingsRequest): Promise<QuipuxSettings> {
  return apiFetch<QuipuxSettings>(base, { method: "PUT", body });
}
