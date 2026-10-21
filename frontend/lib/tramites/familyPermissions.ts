// Permisos Ver / Crear por familia de trámite (Feature #13636 F-F, HU #13665).
//
// Regla única para listados y asistente de creación. Espeja a la API (HU #13664): el listado se filtra por las
// familias con `.read` y crear exige `.create` de la familia del tipo. La autorización real es del backend; esto
// decide qué se muestra.
//
// COMPATIBILIDAD (tokens emitidos antes de la migración #13663, hasta 12 h de vida): si el claim `permissions` NO
// trae ninguno de los 6 slugs por familia pero sí `tramites.read` / `tramites.create`, se trata como «las tres
// familias» para ese permiso — igual que la API con `ModulePermissionEnforcement` apagado. En cuanto el token trae
// aunque sea UNO de los 6 slugs, manda el modo estricto: solo cuentan los slugs de cada familia. Se evalúa sobre el
// conjunto de los 6 (no por Ver/Crear por separado): quien tiene `traspasos.read` y `tramites.create` no hereda
// Crear en las tres familias.
import { type JwtPayload, hasPermission, isSuperAdmin } from "@/lib/auth/jwt";
import type { ProcedureFamily } from "@/lib/api/types/procedure-parametrization";

export const PROCEDURE_FAMILIES: readonly ProcedureFamily[] = ["MATRICULAS", "TRASPASO", "OTROS"];

/** Slugs del catálogo (DDL 145, HU #13663) por familia. */
export const FAMILY_PERMISSION_SLUGS: Record<ProcedureFamily, { read: string; create: string }> = {
  MATRICULAS: { read: "matriculas.read", create: "matriculas.create" },
  TRASPASO: { read: "traspasos.read", create: "traspasos.create" },
  OTROS: { read: "otros-tramites.read", create: "otros-tramites.create" },
};

const BASE_READ = "tramites.read";
const BASE_CREATE = "tramites.create";

const ALL_FAMILY_SLUGS = PROCEDURE_FAMILIES.flatMap((f) => [FAMILY_PERMISSION_SLUGS[f].read, FAMILY_PERMISSION_SLUGS[f].create]);

/** ¿El token ya trae permisos por familia? Si no, es un token «anterior a la migración» (modo compatibilidad). */
export function hasFamilyPermissions(claims: JwtPayload | null): boolean {
  return ALL_FAMILY_SLUGS.some((slug) => hasPermission(claims, slug));
}

/** ¿Puede ver (listar) los trámites de la familia? Sin claims legibles: no. */
export function canReadFamily(claims: JwtPayload | null, family: ProcedureFamily): boolean {
  if (!claims) return false;
  if (isSuperAdmin(claims)) return true;
  if (!hasFamilyPermissions(claims)) return hasPermission(claims, BASE_READ);
  return hasPermission(claims, FAMILY_PERMISSION_SLUGS[family].read);
}

/** ¿Puede crear trámites de la familia? Sin claims legibles: no. */
export function canCreateFamily(claims: JwtPayload | null, family: ProcedureFamily): boolean {
  if (!claims) return false;
  if (isSuperAdmin(claims)) return true;
  if (!hasFamilyPermissions(claims)) return hasPermission(claims, BASE_CREATE);
  return hasPermission(claims, FAMILY_PERMISSION_SLUGS[family].create);
}

export function readableFamilies(claims: JwtPayload | null): ProcedureFamily[] {
  return PROCEDURE_FAMILIES.filter((f) => canReadFamily(claims, f));
}

export function creatableFamilies(claims: JwtPayload | null): ProcedureFamily[] {
  return PROCEDURE_FAMILIES.filter((f) => canCreateFamily(claims, f));
}

/**
 * Familias que se pintan en los agregados por familia (tarjetas KPI y series del inicio y de Reportes, criterio FF-4).
 * Solo se ocultan cuando el token trae permisos por familia (modo estricto): en modo compatibilidad decide la API según
 * su flag, y ocultar las tres dejaría sin tarjetas a un rol que entra al inicio o a Reportes sin `tramites.read`.
 */
export function aggregateFamilies(claims: JwtPayload | null): ProcedureFamily[] {
  if (!claims || !hasFamilyPermissions(claims)) return [...PROCEDURE_FAMILIES];
  return readableFamilies(claims);
}
