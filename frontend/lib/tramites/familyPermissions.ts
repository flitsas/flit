// Permisos Ver / Crear por familia de trámite (Feature #13636 F-F, HU #13665).
//
// Regla única para listados y asistente de creación. Espeja a la API (HU #13664): el listado se filtra por las
// familias con `.read` y crear exige `.create` de la familia del tipo. La autorización real es del backend; esto
// decide qué se muestra.
//
// Bug #14010 H-01: no se hereda `tramites.read` / `tramites.create` cuando el token no trae slugs de familia.
// Esa compatibilidad (tokens anteriores a la migración #13663) hacía indistinguible «todavía no migrado» de
// «las seis familias fueron denegadas», y `/tramites` se abría igual. La API, con la exigencia encendida, ya
// niega en ese caso. Aquí solo cuenta el slug de cada familia (o SuperAdmin).
import { type JwtPayload, hasPermission, isSuperAdmin } from "@/lib/auth/jwt";
import type { ProcedureFamily } from "@/lib/api/types/procedure-parametrization";

export const PROCEDURE_FAMILIES: readonly ProcedureFamily[] = ["MATRICULAS", "TRASPASO", "OTROS"];

/** Slugs del catálogo (DDL 145, HU #13663) por familia. */
export const FAMILY_PERMISSION_SLUGS: Record<ProcedureFamily, { read: string; create: string }> = {
  MATRICULAS: { read: "matriculas.read", create: "matriculas.create" },
  TRASPASO: { read: "traspasos.read", create: "traspasos.create" },
  OTROS: { read: "otros-tramites.read", create: "otros-tramites.create" },
};

const ALL_FAMILY_SLUGS = PROCEDURE_FAMILIES.flatMap((f) => [FAMILY_PERMISSION_SLUGS[f].read, FAMILY_PERMISSION_SLUGS[f].create]);

/** ¿El token trae al menos un slug Ver/Crear de familia? */
export function hasFamilyPermissions(claims: JwtPayload | null): boolean {
  return ALL_FAMILY_SLUGS.some((slug) => hasPermission(claims, slug));
}

/** ¿Puede ver (listar) los trámites de la familia? Sin claims legibles: no. */
export function canReadFamily(claims: JwtPayload | null, family: ProcedureFamily): boolean {
  if (!claims) return false;
  if (isSuperAdmin(claims)) return true;
  return hasPermission(claims, FAMILY_PERMISSION_SLUGS[family].read);
}

/** ¿Puede crear trámites de la familia? Sin claims legibles: no. */
export function canCreateFamily(claims: JwtPayload | null, family: ProcedureFamily): boolean {
  if (!claims) return false;
  if (isSuperAdmin(claims)) return true;
  return hasPermission(claims, FAMILY_PERMISSION_SLUGS[family].create);
}

export function readableFamilies(claims: JwtPayload | null): ProcedureFamily[] {
  return PROCEDURE_FAMILIES.filter((f) => canReadFamily(claims, f));
}

export function creatableFamilies(claims: JwtPayload | null): ProcedureFamily[] {
  return PROCEDURE_FAMILIES.filter((f) => canCreateFamily(claims, f));
}

/**
 * Familias que se pintan en los agregados por familia (tarjetas KPI y series del inicio y de Reportes).
 * Sin claims legibles no se ocultan (aún no hay sesión que evaluar). Con claims, solo las que puede ver.
 */
export function aggregateFamilies(claims: JwtPayload | null): ProcedureFamily[] {
  if (!claims) return [...PROCEDURE_FAMILIES];
  if (isSuperAdmin(claims)) return [...PROCEDURE_FAMILIES];
  return readableFamilies(claims);
}
