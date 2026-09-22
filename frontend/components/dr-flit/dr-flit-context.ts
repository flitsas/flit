import {
  decodeJwtPayload,
  isAdminCompany,
  isOtAdmin,
  isSuperAdmin,
  TOKEN_STORAGE_KEY,
  type JwtPayload,
} from "@/lib/auth/jwt";
import { getToken } from "@/lib/api/client";
import type { NetworkScopePreference } from "@/lib/tramites/network-scope";

/**
 * Rol EFECTIVO con el que DR-FLIT decide a qué API preguntar. Un usuario multi-rol (HU #10506)
 * colapsa a uno solo, en este orden de precedencia:
 *
 *  - `superadmin`: ve todas las compañías por rol (sin `X-Tenant-Id`), nunca «su red».
 *  - `ot_admin`: su universo es la bandeja del organismo, no el listado de trámites del tenant.
 *  - `admin_company`: listado del tenant y, si es cabeza de red con alcance activo, la red.
 *  - `gestor`: Radicador/Operador — solo su compañía (HU #12652).
 */
export type DrFlitRole = "superadmin" | "ot_admin" | "admin_company" | "gestor";

/** Alcance de red vigente (HU #12363). Para quien no es cabeza es siempre `{ active: false }`. */
export interface DrFlitNetworkScope {
  active: boolean;
  /** Un hijo concreto de la red; ausente = toda la red. */
  childTenantId?: string;
}

export interface DrFlitSearchContext {
  role: DrFlitRole;
  tenantId: string | null;
  network: DrFlitNetworkScope;
}

/** Lo que DR-FLIT necesita de `useNetworkScope`: no el hook entero, solo su resultado. */
export interface DrFlitNetworkScopeInput {
  networkActive: boolean;
  scope: NetworkScopePreference;
}

export const DR_FLIT_NO_NETWORK: DrFlitNetworkScope = { active: false };

export function roleFromPayload(payload: JwtPayload | null): DrFlitRole {
  if (isSuperAdmin(payload)) return "superadmin";
  if (isOtAdmin(payload)) return "ot_admin";
  if (isAdminCompany(payload)) return "admin_company";
  return "gestor";
}

/**
 * Resuelve el contexto de búsqueda a partir del payload del JWT y del alcance de red que ya
 * calculó `useNetworkScope` (que a su vez exige AdminCompany + cabeza + confirmación del servidor).
 *
 * La red solo se toma en cuenta para `admin_company`: `useNetworkScope` ya la apaga para
 * SuperAdmin y para roles sin alcance, pero repetir la regla aquí evita que un cambio en el hook
 * abra la red a un rol que no la tiene.
 */
export function resolveDrFlitContext(
  payload: JwtPayload | null,
  network?: DrFlitNetworkScopeInput | null,
): DrFlitSearchContext {
  const role = roleFromPayload(payload);
  const tenantId =
    typeof payload?.tenant_id === "string" && payload.tenant_id.length > 0
      ? payload.tenant_id
      : null;

  const networkScope: DrFlitNetworkScope =
    role === "admin_company" && network?.networkActive && network.scope.mode === "network"
      ? network.scope.childTenantId
        ? { active: true, childTenantId: network.scope.childTenantId }
        : { active: true }
      : DR_FLIT_NO_NETWORK;

  return { role, tenantId, network: networkScope };
}

/** Lee el JWT vigente en cliente (misma resolución que el resto de la SPA). */
export function readJwtPayload(): JwtPayload | null {
  if (typeof window === "undefined") return null;
  const token = getToken() ?? window.localStorage.getItem(TOKEN_STORAGE_KEY);
  return decodeJwtPayload(token);
}
