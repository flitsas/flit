// Par Ver / Crear por tipo de servicio (Feature #13636 F-F, HU #13666). Lógica pura, sin React.
//
// Regla «crear exige ver» (espeja a la API, HU #13664): «Crear» solo se puede tener si se tiene «Ver» de la misma
// familia, y quitar «Ver» quita también «Crear». Familias: Matrículas, Traspasos y Otros trámites; Diagnósticos y
// Comparendos están diferidos. Los slugs son los de `FAMILY_PERMISSION_SLUGS` (HU #13665).
import { FAMILY_PERMISSION_SLUGS, PROCEDURE_FAMILIES } from "@/lib/tramites/familyPermissions";

export const FAMILY_CREATE_REQUIRES_READ_CODE = "FAMILY_CREATE_REQUIRES_READ";

export const FAMILY_CREATE_REQUIRES_READ_MESSAGE =
  "Para habilitar «Crear» primero debe estar habilitado «Ver» en ese tipo de servicio.";

interface SlugAction {
  id: string;
  slug: string;
}

export interface FamilyPairActions<A extends SlugAction> {
  read: A;
  create: A;
}

const FAMILY_SLUG_PAIRS = PROCEDURE_FAMILIES.map((f) => FAMILY_PERMISSION_SLUGS[f]);

/**
 * Nombre de pantalla de los módulos por familia (DDL 145). Lo usan los selectores que solo reciben el código del módulo
 * (roles propios de la compañía, que leen `grantable-permissions`) para mostrar los mismos textos que el Panel SA.
 */
const FAMILY_MODULE_NAMES: Record<string, string> = {
  "tramites-matriculas": "Trámites: Matrículas",
  "tramites-traspasos": "Trámites: Traspasos",
  "tramites-otros": "Trámites: Otros trámites",
};

/** Nombre de pantalla del módulo por familia, o `null` si el código no es de un módulo por familia. */
export function familyModuleName(moduleCode: string): string | null {
  return FAMILY_MODULE_NAMES[moduleCode] ?? null;
}

/**
 * HU #13653: ¿es un módulo de tipo de servicio (Matrículas, Traspasos, Otros trámites)? Los editores de permisos los
 * muestran en su propia pestaña «Tipos de servicio», separados de los demás módulos.
 */
export function isServiceTypeModule(m: { code: string; actions: readonly SlugAction[] }): boolean {
  return familyModuleName(m.code) !== null || findFamilyPair(m.actions) !== null;
}

/** El par Ver / Crear de una familia dentro de las acciones de un módulo, o `null` si el módulo no es de familia. */
export function findFamilyPair<A extends SlugAction>(actions: readonly A[]): FamilyPairActions<A> | null {
  for (const { read, create } of FAMILY_SLUG_PAIRS) {
    const r = actions.find((a) => a.slug === read);
    const c = actions.find((a) => a.slug === create);
    if (r && c) return { read: r, create: c };
  }
  return null;
}

/** ¿La acción es el «Ver» o el «Crear» de alguna familia? */
export function familyRole(slug: string): "read" | "create" | null {
  if (FAMILY_SLUG_PAIRS.some((p) => p.read === slug)) return "read";
  if (FAMILY_SLUG_PAIRS.some((p) => p.create === slug)) return "create";
  return null;
}

/**
 * Configuración por ROL: alterna un permiso del juego `selected` aplicando la regla. Apagar «Ver» apaga también su
 * «Crear»; encender «Crear» sin «Ver» no hace nada (la UI ya lo deshabilita). Devuelve un conjunto nuevo.
 */
export function toggleRolePermission(
  selected: ReadonlySet<string>,
  id: string,
  modules: ReadonlyArray<{ actions: readonly SlugAction[] }>,
): Set<string> {
  const next = new Set(selected);
  const all = modules.flatMap((m) => m.actions);
  const action = all.find((a) => a.id === id);
  const role = action ? familyRole(action.slug) : null;
  const pair = role ? modules.map((m) => findFamilyPair(m.actions)).find((p) => p && (p.read.id === id || p.create.id === id)) : null;

  if (next.has(id)) {
    next.delete(id);
    if (role === "read" && pair) next.delete(pair.create.id);
  } else if (role === "create" && pair && !next.has(pair.read.id)) {
    // «Crear» exige «Ver»: sin él no se enciende.
  } else {
    next.add(id);
  }
  return next;
}

/**
 * Tras un alta/baja masiva (casilla del módulo), deja el juego coherente: ningún «Crear» sin su «Ver».
 */
export function normalizeFamilyPairs(
  selected: ReadonlySet<string>,
  modules: ReadonlyArray<{ actions: readonly SlugAction[] }>,
): Set<string> {
  const next = new Set(selected);
  for (const m of modules) {
    const pair = findFamilyPair(m.actions);
    if (pair && next.has(pair.create.id) && !next.has(pair.read.id)) next.delete(pair.create.id);
  }
  return next;
}

/** ¿El error de la API es el 400 `FAMILY_CREATE_REQUIRES_READ`? (SuperadminApiError, ApiError o cuerpo con `code`). */
export function isFamilyCreateRequiresReadError(error: unknown): boolean {
  const body = (error as { body?: unknown } | null)?.body as { code?: unknown } | string | null | undefined;
  if (body && typeof body === "object" && body.code === FAMILY_CREATE_REQUIRES_READ_CODE) return true;
  if (typeof body === "string" && body.includes(FAMILY_CREATE_REQUIRES_READ_CODE)) return true;
  return error instanceof Error && error.message.includes(FAMILY_CREATE_REQUIRES_READ_CODE);
}
