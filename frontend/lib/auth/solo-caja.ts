// Detección «solo-caja» sobre los claims, sin importar `@/lib/auth/jwt`: la usan componentes del hub cuyas pruebas
// sustituyen ese módulo por un doble parcial. Misma regla que `isSoloCaja` de jwt.ts (único rol `ot_caja`).
type Claims = { roles?: unknown; role_code?: unknown; role?: unknown } | null | undefined;

export function esSoloCaja(payload: Claims): boolean {
  if (!payload) return false;
  const deRoles = Array.isArray(payload.roles)
    ? payload.roles.map((r) => (r as { code?: unknown } | null)?.code).filter((c): c is string => typeof c === "string")
    : [];
  const codigos = (
    deRoles.length > 0
      ? deRoles
      : [payload.role_code, payload.role].flatMap((v) => (Array.isArray(v) ? v : [v])).filter((c): c is string => typeof c === "string" && c !== "")
  ).map((c) => c.toLowerCase());
  return codigos.length > 0 && codigos.every((c) => c === "ot_caja");
}
