/**
 * Mensajes y reporte CSV de la invitación masiva por Excel (Feature #14420 · ADR-0077).
 *
 * El backend responde con códigos estables, en dos niveles:
 * - rechazo del ARCHIVO completo (no se procesa ninguna fila): `template_invalid`, `too_many_rows`…
 * - rechazo de UNA FILA (las demás siguen): `EMAIL_ALREADY_IN_USE`, `ROLE_NOT_ASSIGNABLE`…
 * Traducirlos aquí evita enseñarle al usuario un código técnico en pantalla.
 */
import { ApiError } from "@/lib/api/types";
import {
  BULK_INVITE_MAX_FILE_BYTES,
  BULK_INVITE_MAX_ROWS,
  type BulkInvitationRowResult,
} from "@/lib/api/bulk-invitations-client";
import { EMAIL_ALREADY_ASSOCIATED_MESSAGE, emailConflictErrorCode } from "@/lib/users/emailConflict";

/** `Template-Invalid`, `TEMPLATE_INVALID` y `templateInvalid` son el mismo código. */
function normalizeCode(code: string): string {
  return code
    .trim()
    .replace(/([a-z0-9])([A-Z])/g, "$1_$2")
    .replace(/[\s-]+/g, "_")
    .toUpperCase();
}

const MAX_MB = BULK_INVITE_MAX_FILE_BYTES / (1024 * 1024);

// ── Archivo completo ───────────────────────────────────────────────────────────────────────────

const FILE_ERROR_MESSAGES: Record<string, string> = {
  TEMPLATE_INVALID:
    "El archivo no coincide con la plantilla. No cambies, renombres ni muevas las columnas de la primera fila; descarga la plantilla de nuevo si tienes dudas.",
  MISSING_COLUMNS:
    "Al archivo le faltan columnas de la plantilla (Nombre completo, Correo y Rol). Descarga la plantilla de nuevo.",
  TOO_MANY_ROWS: `El archivo supera las ${BULK_INVITE_MAX_ROWS} filas permitidas. Divídelo en varios archivos.`,
  FILE_TOO_LARGE: `El archivo pesa más de ${MAX_MB} MB. Reduce las filas o divídelo en varios archivos.`,
  INVALID_FILE: "El archivo no es un Excel (.xlsx) válido.",
  EMPTY_FILE: "El archivo no tiene filas de datos: está solo el encabezado.",
  NO_ROWS: "El archivo no tiene filas de datos: está solo el encabezado.",
  TARGET_TENANT_REQUIRED: "Debes seleccionar el destino de los usuarios antes de cargar el archivo.",
  RATE_LIMITED: "Hiciste demasiadas validaciones seguidas. Espera un momento e inténtalo de nuevo.",
};

/** Mensaje claro para un rechazo del archivo o de la petición (nunca contiene datos del archivo). */
export function bulkInviteRequestErrorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    const code = emailConflictErrorCode(err.body);
    const known = code ? FILE_ERROR_MESSAGES[normalizeCode(code)] : undefined;
    if (known) return known;
    switch (err.status) {
      case 400:
      case 413:
      case 415:
      case 422:
        return "No se pudo procesar el archivo. Verifica que sea la plantilla en formato .xlsx e inténtalo de nuevo.";
      case 401:
        return "Tu sesión no es válida. Vuelve a iniciar sesión.";
      case 403:
        return "No tienes permiso para invitar usuarios masivamente en este destino.";
      case 404:
        return "No se encontró el destino seleccionado.";
      case 429:
        return FILE_ERROR_MESSAGES.RATE_LIMITED;
      default:
        return "No se pudo procesar el archivo. Inténtalo de nuevo en unos minutos.";
    }
  }
  return "No se pudo procesar el archivo. Inténtalo de nuevo.";
}

/** Mensaje para un fallo al descargar la plantilla (403 sin permiso, destino inexistente, red). */
export function bulkInviteTemplateErrorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 401) return "Tu sesión no es válida. Vuelve a iniciar sesión.";
    if (err.status === 403) return "No tienes permiso para invitar usuarios masivamente en este destino.";
    if (err.status === 404) return "No se encontró el destino seleccionado.";
  }
  return "No se pudo descargar la plantilla. Inténtalo de nuevo.";
}

/**
 * Cuando el commit no obtuvo respuesta (red caída, timeout o puerta de enlace 502/503/504) no se
 * sabe qué filas se procesaron: hay que avisarlo, y NO ofrecer «reintentar» a ciegas.
 */
export function isBulkInviteOutcomeUncertain(err: unknown): boolean {
  if (err instanceof ApiError) return err.status === 502 || err.status === 503 || err.status === 504;
  return err instanceof Error && err.name === "BulkInvitationsNetworkError";
}

export const BULK_INVITE_UNCERTAIN_MESSAGE =
  "No recibimos la respuesta del servidor. Algunas invitaciones pueden haberse creado: revisa la lista de usuarios (invitaciones pendientes) antes de volver a cargar el archivo.";

/** Validación local antes de enviar (AC2): extensión y tamaño. El backend repite todo. */
export function validateBulkInviteFile(file: File): string | null {
  if (!file.name.toLowerCase().endsWith(".xlsx")) {
    return "El archivo debe ser un Excel con extensión .xlsx.";
  }
  if (file.size === 0) return "El archivo está vacío.";
  if (file.size > BULK_INVITE_MAX_FILE_BYTES) {
    return `El archivo pesa más de ${MAX_MB} MB. Reduce las filas o divídelo en varios archivos.`;
  }
  return null;
}

// ── Fila ───────────────────────────────────────────────────────────────────────────────────────

const ROW_ERROR_MESSAGES: Record<string, string> = {
  // Indistinguible a propósito (HU #11580): cuenta existente, invitación pendiente o usuario eliminado.
  EMAIL_ALREADY_IN_USE: EMAIL_ALREADY_ASSOCIATED_MESSAGE,
  DUPLICATE_EMAIL_IN_FILE: "El correo está repetido en el archivo.",
  EMAIL_REQUIRED: "Falta el correo.",
  INVALID_EMAIL: "El correo no tiene un formato válido.",
  EMAIL_INVALID: "El correo no tiene un formato válido.",
  NAME_REQUIRED: "Falta el nombre completo.",
  FULL_NAME_REQUIRED: "Falta el nombre completo.",
  ROLE_REQUIRED: "Falta el rol.",
  ROLE_NOT_FOUND: "El rol no existe en el catálogo de la plantilla.",
  UNKNOWN_ROLE: "El rol no existe en el catálogo de la plantilla.",
  ROLE_AMBIGUOUS: "El rol coincide con más de uno del catálogo. Usa el nombre exacto de la plantilla.",
  ROLE_NOT_ASSIGNABLE: "No puedes asignar ese rol en este destino.",
  ROLE_NOT_ALLOWED: "No puedes asignar ese rol en este destino.",
  ROLE_OUT_OF_SCOPE: "No puedes asignar ese rol en este destino.",
  SUPERADMIN_ROLE_NOT_ASSIGNABLE: "El rol Super Administrador no se puede asignar por carga masiva.",
  ROLE_TARGET_ENTITY_TYPE_MISMATCH: "El rol no aplica al tipo de destino (compañía u organismo).",
  ADMIN_ROLE_NOT_ASSIGNABLE: "Tu perfil no puede asignar roles de administrador.",
  MULTIPLE_ROLES_NOT_ALLOWED: "Solo se admite un rol por fila.",
  NOT_PROCESSED: "No se alcanzó a procesar: se agotó el tiempo. Vuelve a cargar el archivo; los ya invitados se omitirán.",
  TIMEOUT: "No se alcanzó a procesar: se agotó el tiempo. Vuelve a cargar el archivo; los ya invitados se omitirán.",
};

/** Motivo legible de una fila rechazada o no procesada. */
export function bulkInviteRowReason(row: Pick<BulkInvitationRowResult, "status" | "errorCode">): string {
  const code = row.errorCode ? normalizeCode(row.errorCode) : null;
  if (code && ROW_ERROR_MESSAGES[code]) return ROW_ERROR_MESSAGES[code];
  if (row.status === "not_processed") return ROW_ERROR_MESSAGES.NOT_PROCESSED;
  return code ? `La fila no es válida (${code}).` : "La fila no es válida.";
}

// ── CSV de errores ─────────────────────────────────────────────────────────────────────────────

/**
 * Neutraliza el prefijo de fórmula (OWASP CSV Injection). Un valor que empieza por `=`, `+`, `-`
 * o `@` —o por tab / retorno de carro— lo ejecuta Excel al abrir el archivo, y aquí los valores
 * salen de un Excel que cargó el usuario, o sea que no son de fiar.
 */
export function neutralizeCsvFormula(value: string): string {
  return /^[=+\-@\t\r]/.test(value) ? `'${value}` : value;
}

function csvCell(value: string | number | null | undefined): string {
  const text = value === null || value === undefined ? "" : String(value);
  return `"${neutralizeCsvFormula(text).replace(/"/g, '""')}"`;
}

/** Filas que NO quedaron invitadas: las rechazadas y las no procesadas. */
export function bulkInviteErrorRows(rows: readonly BulkInvitationRowResult[]): BulkInvitationRowResult[] {
  return rows.filter((r) => r.status === "rejected" || r.status === "not_processed");
}

/**
 * CSV con las filas que fallaron: fila, correo, nombre, rol y motivo. Separador `;` y BOM porque el
 * destino real es Excel en español (con `,` mete la fila entera en una celda y sin BOM rompe las tildes).
 */
export function buildBulkInviteErrorsCsv(rows: readonly BulkInvitationRowResult[]): string {
  const header = ["Fila", "Correo", "Nombre completo", "Rol", "Motivo"].map(csvCell).join(";");
  const body = bulkInviteErrorRows(rows).map((r) =>
    [r.rowNumber, r.email, r.fullName, r.roleName, bulkInviteRowReason(r)].map(csvCell).join(";"),
  );
  return `\uFEFF${[header, ...body].join("\r\n")}`;
}
