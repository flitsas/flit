// Resolución de módulos de la SPA (home). Extraído de app/page.tsx para poder probar la
// regla de "Ayuda universal" sin montar toda la página.
import type { ModuleId } from "@/components/atom/Shell";

/** Todos los módulos conocidos (fallback cuando aún no hay permisos RBAC cargados). */
export const ALL_MODULE_IDS: ModuleId[] = [
  "dashboard",
  "tramites",
  "reportes",
  "validaciones",
  "usuarios",
  "ayuda",
  "rbac",
];

/**
 * Módulos de soporte universal: siempre navegables aunque RBAC no los incluya. "Ayuda"
 * no es una función con permiso; debe abrirse desde el dock en cualquier pantalla.
 */
export const UNIVERSAL_MODULE_IDS: ModuleId[] = ["ayuda"];

/**
 * Construye la lista de módulos válidos para la SPA: los accesibles por RBAC más los
 * universales (sin duplicar). Si aún no hay permisos, devuelve todos los conocidos.
 */
export function buildValidModules(accessibleCodes: ModuleId[]): ModuleId[] {
  if (accessibleCodes.length === 0) {
    return [...ALL_MODULE_IDS];
  }
  const merged = [...accessibleCodes];
  for (const id of UNIVERSAL_MODULE_IDS) {
    if (!merged.includes(id)) {
      merged.push(id);
    }
  }
  return merged;
}

/** Normaliza el `?m=` de la URL a un módulo válido; cae a "dashboard" si no lo es. */
export function parseModule(raw: string | null, valid: ModuleId[]): ModuleId {
  const allowed = valid.length > 0 ? valid : ALL_MODULE_IDS;
  return allowed.includes(raw as ModuleId) ? (raw as ModuleId) : "dashboard";
}
