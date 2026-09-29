/**
 * Feature #12917 (Épica #12718, ADR-0060) — lógica pura del caso de soporte de DR. FLIT: borrador
 * prellenado, validaciones del formulario y de adjuntos, y armado del cuerpo que va al backend.
 * Sin React ni red: lo usan la máquina de estados, el hook y el formulario.
 */
import {
  DR_FLIT_SUPPORT_ALLOWED_EXTENSIONS,
  DR_FLIT_SUPPORT_MAX_ATTACHMENTS,
  DR_FLIT_SUPPORT_MAX_FILE_BYTES,
  type DrFlitSupportCaseRequest,
} from "@/lib/api/dr-flit-client";
import type { DrFlitSupportCaseDraft, DrFlitSupportFrequency } from "./dr-flit-chat-types";

/** Límites del contrato §5.2 (el backend los revalida). */
export const DR_FLIT_SUPPORT_LIMITS = {
  titulo: 200,
  detalle: 4000,
  resultadoEsperado: 2000,
  telefono: 30,
} as const;

/** Datos que la plataforma ya conoce del usuario (HU #12929 AC1). */
export interface DrFlitSupportContact {
  name?: string | null;
  email?: string | null;
  company?: string | null;
}

/** DD/MM/YYYY en hora Colombia (formato estándar de fechas de la plataforma). */
export function formatBogotaDate(now: Date = new Date()): string {
  return new Intl.DateTimeFormat("es-CO", {
    timeZone: "America/Bogota",
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  }).format(now);
}

/**
 * HU #12929 AC1 — borrador con nombre, correo y compañía prellenados y la fecha fijada. El usuario
 * completa el resto.
 */
export function createSupportDraft(
  contact: DrFlitSupportContact,
  affectedModule: string | null,
  now: Date = new Date(),
): DrFlitSupportCaseDraft {
  return {
    nombre: contact.name?.trim() ?? "",
    email: contact.email?.trim() ?? "",
    telefono: "",
    compania: contact.company?.trim() ?? "",
    fecha: formatBogotaDate(now),
    detalle: "",
    resultadoEsperado: "",
    frecuencia: "",
    titulo: "",
    prioridad: "",
    adjuntar: null,
    attachments: [],
    affectedModule,
  };
}

export type DrFlitSupportField =
  | "nombre"
  | "email"
  | "telefono"
  | "compania"
  | "detalle"
  | "resultadoEsperado"
  | "frecuencia"
  | "titulo"
  | "prioridad";

export type DrFlitSupportErrors = Partial<Record<DrFlitSupportField, string>>;

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** HU #12929 AC2 — campos faltantes o inválidos. Vacío = se puede continuar. */
export function validateSupportDraft(draft: DrFlitSupportCaseDraft): DrFlitSupportErrors {
  const errors: DrFlitSupportErrors = {};
  const required = (field: DrFlitSupportField, value: string, message: string) => {
    if (!value.trim()) errors[field] = message;
  };

  required("nombre", draft.nombre, "Escribe tu nombre.");
  required("email", draft.email, "Escribe tu correo.");
  if (draft.email.trim() && !EMAIL_RE.test(draft.email.trim())) errors.email = "El correo no es válido.";
  required("compania", draft.compania, "Escribe tu compañía.");
  required("detalle", draft.detalle, "Cuéntanos qué pasó.");
  required("resultadoEsperado", draft.resultadoEsperado, "Cuéntanos qué esperabas que pasara.");
  required("titulo", draft.titulo, "Ponle un título al caso.");
  if (!draft.frecuencia) errors.frecuencia = "Elige con qué frecuencia pasa.";
  if (!draft.prioridad) errors.prioridad = "Elige la prioridad.";

  if (draft.titulo.trim().length > DR_FLIT_SUPPORT_LIMITS.titulo)
    errors.titulo = `El título admite máximo ${DR_FLIT_SUPPORT_LIMITS.titulo} caracteres.`;
  if (draft.detalle.trim().length > DR_FLIT_SUPPORT_LIMITS.detalle)
    errors.detalle = `El detalle admite máximo ${DR_FLIT_SUPPORT_LIMITS.detalle} caracteres.`;
  if (draft.resultadoEsperado.trim().length > DR_FLIT_SUPPORT_LIMITS.resultadoEsperado)
    errors.resultadoEsperado = `Admite máximo ${DR_FLIT_SUPPORT_LIMITS.resultadoEsperado} caracteres.`;
  if (draft.telefono.trim().length > DR_FLIT_SUPPORT_LIMITS.telefono)
    errors.telefono = `El teléfono admite máximo ${DR_FLIT_SUPPORT_LIMITS.telefono} caracteres.`;

  return errors;
}

/**
 * HU #12929 AC4 — rechazo local antes de subir: extensión, tamaño y cantidad con los mismos defaults
 * del backend. Devuelve el motivo o `null` si el archivo se puede subir.
 */
export function validateAttachmentFile(file: Pick<File, "name" | "size">, currentCount: number): string | null {
  if (currentCount >= DR_FLIT_SUPPORT_MAX_ATTACHMENTS)
    return `Puedes adjuntar máximo ${DR_FLIT_SUPPORT_MAX_ATTACHMENTS} archivos.`;
  const dot = file.name.lastIndexOf(".");
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : "";
  if (!(DR_FLIT_SUPPORT_ALLOWED_EXTENSIONS as readonly string[]).includes(extension))
    return `«${file.name}» no es un tipo permitido. Usa imágenes (PNG, JPG, WEBP), PDF o TXT.`;
  if (file.size <= 0) return `«${file.name}» está vacío.`;
  if (file.size > DR_FLIT_SUPPORT_MAX_FILE_BYTES)
    return `«${file.name}» pesa más de ${DR_FLIT_SUPPORT_MAX_FILE_BYTES / (1024 * 1024)} MB.`;
  return null;
}

/**
 * Módulo afectado a partir del módulo activo del Shell (`routeScope = "{pathname}|{módulo}"`). Mejor
 * esfuerzo con los valores del picklist de soporte; sin coincidencia va `null` y el backend aplica su
 * default configurado.
 */
const MODULE_BY_SCOPE: Record<string, string> = {
  validaciones: "Validación de Identidad",
  reportes: "Reportes",
  "reportes-detallados": "Reportes",
  usuarios: "Usuarios",
  "log-qx": "Qx",
  "admin-quipux": "Qx",
};

export function resolveAffectedModule(routeScope: string | null | undefined): string | null {
  if (!routeScope) return null;
  const [pathname = "", active = ""] = routeScope.split("|");
  const byModule = MODULE_BY_SCOPE[active];
  if (byModule) return byModule;
  if (active.startsWith("admin-") || pathname.startsWith("/admin") || pathname.startsWith("/superadmin"))
    return "Administradores";
  return null;
}

/** Cuerpo del POST con lo que el usuario confirmó. Nunca incluye nada del chat. */
export function toSupportCaseRequest(draft: DrFlitSupportCaseDraft): DrFlitSupportCaseRequest {
  return {
    nombre: draft.nombre.trim(),
    email: draft.email.trim(),
    telefono: draft.telefono.trim() || null,
    compania: draft.compania.trim(),
    detalle: draft.detalle.trim(),
    resultadoEsperado: draft.resultadoEsperado.trim(),
    frecuencia: draft.frecuencia,
    titulo: draft.titulo.trim(),
    prioridad: draft.prioridad,
    affectedModule: draft.affectedModule,
    attachmentIds: draft.adjuntar ? draft.attachments.map((a) => a.id) : [],
  };
}

export const DR_FLIT_FREQUENCY_LABELS: Record<DrFlitSupportFrequency, string> = {
  una_vez: "Una vez",
  a_veces: "A veces",
  siempre: "Siempre",
};
