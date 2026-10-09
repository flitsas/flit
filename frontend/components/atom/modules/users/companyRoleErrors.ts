// HU #13441/#13443 — Mensajes de los errores de la gestión de roles propios de la compañía.
// El backend responde { code, message, permissions? }; se traduce el código a un texto de pantalla estable.
import { ApiError } from "@/lib/api/types";

const MESSAGES: Record<string, string> = {
  PERMISSION_PLATFORM_ONLY: "Hay permisos de plataforma, que solo gestiona FLIT. Quítalos e inténtalo de nuevo.",
  PERMISSION_MODULE_NOT_ENABLED: "Hay permisos de un módulo que no está habilitado para tu compañía. Quítalos e inténtalo de nuevo.",
  PERMISSION_NOT_HELD: "Solo puedes otorgar permisos que tú mismo tienes. Quita los que no te corresponden.",
  PERMISSION_NOT_FOUND: "Alguno de los permisos ya no existe. Cierra la ventana y vuelve a abrirla.",
  INVALID_ROLE_INPUT: "El código (2 a 50 caracteres: letras, números, punto, guion o guion bajo) y el nombre (hasta 100) son obligatorios.",
  INVALID_TARGET_ENTITY_TYPE: "Una compañía solo puede crear roles de compañía.",
  INVALID_ROLE_PRODUCT: "El producto del rol no es válido.",
  ROLE_PERMISSION_PRODUCT_MISMATCH: "Un rol solo puede incluir permisos de su mismo producto.",
  ROLE_CODE_DUPLICATE: "Ya existe un rol con ese código en tu compañía o en el catálogo de FLIT. Usa otro.",
  ROLE_HAS_ACTIVE_USERS: "El rol está asignado a usuarios. Cámbialos de rol antes de eliminarlo.",
  ROLE_READ_ONLY: "Los roles globales de FLIT son de solo lectura.",
};

const GENERIC = "No se pudo completar la operación. Inténtalo de nuevo.";

/** Código de negocio del error (`body.code`) o `null` si no es un error de la API con código. */
export function companyRoleErrorCode(err: unknown): string | null {
  if (!(err instanceof ApiError)) return null;
  const code = (err.body as { code?: string } | undefined)?.code;
  return typeof code === "string" ? code : null;
}

export function companyRoleErrorMessage(err: unknown): string {
  if (!(err instanceof ApiError)) return GENERIC;
  const body = err.body as { code?: string; permissions?: string[] | null } | undefined;
  const base = body?.code ? MESSAGES[body.code] : undefined;
  if (base) {
    const slugs = body?.permissions?.filter(Boolean) ?? [];
    return slugs.length > 0 ? `${base} (${slugs.join(", ")})` : base;
  }
  if (err.status === 404) return "El rol ya no existe. Recarga el listado.";
  // 403 sin un código conocido: no se afirma quién puede gestionar roles (puede ser sesión vencida o permisos).
  if (err.status === 403) return "No tienes permiso para esta acción.";
  return GENERIC;
}
