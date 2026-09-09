/**
 * Navegación y gate de visibilidad del módulo "Generación documental"
 * (HU-01 · Feature #12201).
 *
 * Tres vistas planas (Certificado RUES / Transferencia / Historial) con una barra de
 * pestañas local, mismo patrón que `improntas-nav.ts`: cada feature admin mantiene su
 * propia barra en vez de compartir un componente entre módulos.
 */

/** Código del módulo en `/api/v1/security/modules` (seeder `SeedGeneracionDocumentalPermissionsAsync`). */
export const GENERACION_DOCUMENTAL_MODULE_CODE = "generacion-documental";

/** Raíz de las rutas del módulo. */
export const GENERACION_DOCUMENTAL_BASE_PATH = "/admin/generacion-documental";

export type GeneracionDocumentalTabId = "rues" | "transferencia" | "lotes" | "historial";

export interface GeneracionDocumentalTab {
  id: GeneracionDocumentalTabId;
  label: string;
}

export const GENERACION_DOCUMENTAL_TABS: GeneracionDocumentalTab[] = [
  { id: "rues", label: "Certificado RUES" },
  { id: "transferencia", label: "Transferencia" },
  // Cuarta pestaña (HU #12224). Las tres primeras son las de CF-01; esta es la tercera FORMA DE
  // GENERAR, no una vista de detalle —por eso sí es pestaña y el seguimiento de un lote concreto
  // no lo es: aquel cuelga de esta, como una ficha cuelga de un listado.
  { id: "lotes", label: "Carga masiva" },
  { id: "historial", label: "Historial" },
];

export function generacionDocumentalTabPath(tab: GeneracionDocumentalTabId): string {
  return tab === "rues" ? GENERACION_DOCUMENTAL_BASE_PATH : `${GENERACION_DOCUMENTAL_BASE_PATH}/${tab}`;
}

/**
 * Visibilidad de la entrada del dock (R12, AC «Un AdminCompany ve la entrada del dock»).
 *
 * <p>Se resuelve por los MÓDULOS ACCESIBLES del usuario (`/api/v1/security/modules`, vía
 * `useAccessibleModules` → `visibleModuleCodes`), NUNCA por `currentUser?.isSuperAdmin`:
 * el resto de entradas admin del dock cuelgan de ese flag y con él un AdminCompany con el
 * permiso `generacion-documental.read` jamás vería el módulo.</p>
 *
 * <p>`undefined` significa «todavía no sé qué módulos tiene» (el Shell se renderiza sin el
 * prop o el hook aún no resolvió): se decide NO mostrar, para no filtrar una entrada a
 * quien no la tiene. Cuando la lista llega, la entrada aparece.</p>
 */
export function canSeeGeneracionDocumental(visibleModuleCodes?: string[]): boolean {
  return Array.isArray(visibleModuleCodes) && visibleModuleCodes.includes(GENERACION_DOCUMENTAL_MODULE_CODE);
}

/** Formatea una fecha ISO del historial para mostrarla en tabla (huso del navegador). */
export function formatGeneracionDocumentalDate(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) {
    return iso;
  }
  return parsed.toLocaleString("es-CO", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/**
 * Etiquetas de los tipos de documento del módulo. No son estados: el contrato de estados
 * vive, entero y solo, en `status-labels.ts` (CF-21).
 */
export const GENERACION_DOCUMENTAL_TYPE_LABELS: Record<string, string> = {
  certificado_rues: "Certificado RUES",
  transferencia_dominio_generada: "Transferencia de dominio",
};

/** Etiqueta legible del tipo de documento; ante un tipo desconocido devuelve el código. */
export function generacionDocumentalTypeLabel(documentType: string): string {
  return GENERACION_DOCUMENTAL_TYPE_LABELS[documentType] ?? documentType;
}
