/**
 * HU #13139 — qué puede hacer el usuario sobre cada mandatario de la lista. El servidor manda:
 * calcula `puedeEditar` y `puedeEliminar` por rol y origen (HU #13134); aquí solo se lee. Un
 * mandatario sin banderas (respuesta anterior al cambio) no bloquea la UI: el servidor responde 403
 * si el actor no puede.
 */
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import {
  isAdminCompany,
  isOtAdmin,
  isSuperAdmin,
  type JwtPayload,
} from "@/lib/auth/jwt";

export const LEYENDA_CONFIGURADO_POR_ORGANISMO = "Configurado por el organismo de tránsito";

export function puedeEditarMandatario(signer: Pick<MandateSigner, "puedeEditar">): boolean {
  return signer.puedeEditar !== false;
}

export function puedeEliminarMandatario(signer: Pick<MandateSigner, "puedeEliminar">): boolean {
  return signer.puedeEliminar !== false;
}

/**
 * Candado: lo configuró el organismo y este actor no puede tocarlo (Admin de Compañía). Quien sí
 * puede (Admin OT, Super Admin) ve el mandatario normal, con sus acciones.
 */
export function tieneCandadoDelOrganismo(
  signer: Pick<MandateSigner, "origin" | "puedeEditar" | "puedeEliminar">,
): boolean {
  return signer.origin === "organismo" && !puedeEditarMandatario(signer) && !puedeEliminarMandatario(signer);
}

/**
 * El Gestor/Radicador no ve crear, editar ni eliminar. Sin token legible no se oculta nada (las
 * pruebas y el SSR no lo tienen): la autorización real es del servidor.
 */
export function puedeCrearMandatarios(payload: JwtPayload | null): boolean {
  if (!payload) return true;
  return isSuperAdmin(payload) || isAdminCompany(payload) || isOtAdmin(payload);
}
