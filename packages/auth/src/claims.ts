// SessionUser a partir de los claims del access token (contrato §2 y §8). No se verifica la firma aquí: el token
// llegó por el canal de servidor desde el token endpoint, y la API lo valida en cada llamada.
import { base64UrlDecode } from "./crypto";
import type { SessionUser } from "./types";

type Claims = Record<string, unknown>;

export function decodeClaims(accessToken: string): Claims {
  const payload = accessToken.split(".")[1];
  if (!payload) throw new Error("El access token no es un JWT.");
  return JSON.parse(new TextDecoder().decode(base64UrlDecode(payload))) as Claims;
}

export function sessionUser(accessToken: string): SessionUser {
  const c = decodeClaims(accessToken);
  const roles = asArray(c.roles).filter((r): r is { id: string; code: string } => typeof r === "object" && r !== null && "code" in r);
  const roleCodes = [...asArray(c.role), ...asArray(c.role_code), ...roles.map((r) => r.code)].map(String);
  return {
    id: str(c.sub),
    email: str(c.email),
    product: Array.isArray(c.aud) ? str(c.aud[0]) : str(c.aud),
    domain: str(c.dom) || "flit",
    tenant: {
      id: str(c.tenant_id),
      name: str(c.tenant_name),
      nit: str(c.company_nit),
      type: str(c.tenant_type),
      entityType: str(c.entity_type) === "TRANSIT_OFFICE" ? "TRANSIT_OFFICE" : "COMPANY",
      parentId: c.parent_tenant_id ? str(c.parent_tenant_id) : null,
      isGroupParent: c.is_group_parent === true || c.is_group_parent === "true",
    },
    roles,
    permissions: asArray(c.permissions).map(String),
    // Regla multi-rol del contrato §2.1: cualquiera de los claims de rol.
    isSuperAdmin: roleCodes.includes("SuperAdmin"),
    expiresAt: typeof c.exp === "number" ? c.exp : 0,
  };
}

function asArray(value: unknown): unknown[] {
  if (value === undefined || value === null) return [];
  return Array.isArray(value) ? value : [value];
}

function str(value: unknown): string {
  return typeof value === "string" ? value : value === undefined || value === null ? "" : String(value);
}
