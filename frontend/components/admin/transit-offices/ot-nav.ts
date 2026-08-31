/** Rutas del hub consola OT (HU #10236). */
export type OtHubTabId =
  | "tramites"
  | "webhooks"
  | "client-procedures"
  | "rules"
  | "documents"
  | "requirements"
  | "plate-ranges"
  | "usuarios"
  | "reportes"
  | "mandatos";

export interface OtHubTab {
  id: OtHubTabId;
  label: string;
  segment: OtHubTabId;
}

/**
 * Pestañas del hub (navegación interna). Visibles en OtTabBar solo para SuperAdmin;
 * Admin OT las consume desde el dock (sin barra de pestañas).
 *
 * Labels alineados al dock Admin OT: Trámites (ex "Trámites clientes"), Preasignación.
 * "Trámites"/"Webhooks" (ids legacy) siguen fuera de la oferta; rutas por URL siguen vivas.
 */
export const OT_HUB_TABS: OtHubTab[] = [
  { id: "client-procedures", label: "Trámites", segment: "client-procedures" },
  { id: "rules", label: "Reglas", segment: "rules" },
  { id: "documents", label: "Documentos", segment: "documents" },
  { id: "requirements", label: "Requisitos", segment: "requirements" },
  { id: "plate-ranges", label: "Preasignación", segment: "plate-ranges" },
  { id: "usuarios", label: "Usuarios", segment: "usuarios" },
  { id: "reportes", label: "Reportes", segment: "reportes" },
  { id: "mandatos", label: "Mandatos", segment: "mandatos" },
];

/** Keys del dock Admin OT (agrupación distinta a SuperAdmin). */
export const OT_ADM_DOCK = {
  rules: "ot-adm-rules",
  documents: "ot-adm-documents",
  requirements: "ot-adm-requirements",
  tramites: "ot-adm-tramites",
  preasignacion: "ot-adm-preasignacion",
  usuarios: "ot-adm-usuarios",
  reportes: "ot-adm-reportes",
  mandatos: "ot-adm-mandatos",
} as const;

const OT_PROFILE_ID_KEY = "flit-ot-transit-office-id";

export function otHubModulePath(transitOfficeId: string, tab: OtHubTabId): string {
  return `/admin/transit-offices/${transitOfficeId}/${tab}`;
}

export function otHubListPath(): string {
  return "/admin/transit-offices";
}

/** Extrae el id de OT desde `/admin/transit-offices/{id}/…`. */
export function extractTransitOfficeIdFromPath(pathname: string): string | null {
  const match = pathname.match(/^\/admin\/transit-offices\/([^/?#]+)/);
  const id = match?.[1];
  if (!id) return null;
  return id;
}

export function rememberOtTransitOfficeId(id: string): void {
  if (typeof window === "undefined") return;
  try {
    window.sessionStorage.setItem(OT_PROFILE_ID_KEY, id);
  } catch {
    /* private mode / quota */
  }
}

function readCachedOtTransitOfficeId(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return window.sessionStorage.getItem(OT_PROFILE_ID_KEY);
  } catch {
    return null;
  }
}

/**
 * Id del organismo de la sesión, con la misma caché de sesión que usa el dock: el perfil solo se
 * pide una vez por pestaña. Lo consumen tanto la navegación del hub como la vista de inicio del
 * Admin OT (HU #11940), que necesita el id sin querer navegar a ningún lado.
 */
export async function resolveOtTransitOfficeId(
  fetchProfileId: () => Promise<string>,
): Promise<string> {
  const cached = readCachedOtTransitOfficeId();
  if (cached) return cached;
  const id = await fetchProfileId();
  rememberOtTransitOfficeId(id);
  return id;
}

/**
 * Resuelve la URL de un módulo del hub OT.
 * - Si la ruta ya tiene `{id}`, navega ahí.
 * - Admin OT: usa perfil (con caché de sesión).
 * - SuperAdmin sin OT en ruta: vuelve al listado.
 */
export async function resolveOtHubHref(
  tab: OtHubTabId,
  pathname: string,
  mode: "ot_admin" | "superadmin",
  fetchProfileId: () => Promise<string>,
): Promise<string> {
  const fromPath = extractTransitOfficeIdFromPath(pathname);
  if (fromPath) {
    rememberOtTransitOfficeId(fromPath);
    return otHubModulePath(fromPath, tab);
  }

  if (mode === "ot_admin") {
    return otHubModulePath(await resolveOtTransitOfficeId(fetchProfileId), tab);
  }

  return otHubListPath();
}

export function isOtHubSegmentActive(pathname: string, segment: OtHubTabId): boolean {
  const id = extractTransitOfficeIdFromPath(pathname);
  if (!id) return false;
  return pathname.includes(`/admin/transit-offices/${id}/${segment}`);
}

/** Búsqueda insensible a mayúsculas y tildes (patrón OTMatrix). */
export function foldOtSearch(value: string): string {
  return value
    .normalize("NFD")
    .replace(/\p{M}/gu, "")
    .toLowerCase();
}

export function matchesOtOfficeSearch(
  office: { name: string; code: string },
  term: string,
): boolean {
  const folded = foldOtSearch(term);
  if (!folded) {
    return true;
  }
  return (
    foldOtSearch(office.name).includes(folded) || foldOtSearch(office.code).includes(folded)
  );
}
