import { COPY } from "@/lib/copy/copy-catalog";

/** Rutas del hub consola OT (HU #10236). */
export type OtHubTabId =
  | "webhooks"
  | "client-procedures"
  | "rules"
  | "documents"
  | "requirements"
  | "usuarios"
  | "reportes"
  | "mandatos"
  | "imprint-validation"
  | "revocation-requests"
  | "configuracion";

export interface OtHubTab {
  id: OtHubTabId;
  label: string;
  segment: OtHubTabId;
}

/**
 * Pestañas del hub (navegación interna). Visibles en OtTabBar solo para SuperAdmin;
 * Admin OT las consume desde el dock (sin barra de pestañas).
 *
 * Labels alineados al dock Admin OT: Trámites (ex "Trámites clientes").
 * "Webhooks" (id legacy) sigue fuera de la oferta; su ruta por URL sigue viva.
 * HU #12850 (Feature #12846) — Preasignación se retiró: la consola de rangos ya no existe en
 * backend (HU-A1) y la asignación de placa del trámite siempre reserva fuera de rango.
 * HU #12857 (Feature #12846) — la ruta legacy `[id]/tramites` (TramitesSuperSection) se retiró:
 * duplicaba sin enlace de menú lo que ya cubre "Configuración" (modo Dashboard/QX, ventana de
 * revocatoria, feature flags operativos). El id `tramites` salió de `OtHubTabId`: ya no hay
 * pantalla que lo resuelva. El id vigente de la bandeja sigue siendo `client-procedures`.
 */
export const OT_HUB_TABS: OtHubTab[] = [
  { id: "client-procedures", label: COPY.B21Tramites, segment: "client-procedures" },
  { id: "rules", label: "Reglas", segment: "rules" },
  { id: "documents", label: "Documentos", segment: "documents" },
  { id: "requirements", label: "Requisitos", segment: "requirements" },
  { id: "usuarios", label: COPY.B21Usuarios, segment: "usuarios" },
  { id: "reportes", label: COPY.B21Reportes, segment: "reportes" },
  { id: "mandatos", label: "Mandatos", segment: "mandatos" },
  { id: "imprint-validation", label: "Validar impronta", segment: "imprint-validation" },
  // HU #12578 (Feature #12565) — vista dedicada "Revocatorias": todos los intentos de solicitud de
  // revocatoria de los trámites del organismo, en cualquier sub-estado.
  { id: "revocation-requests", label: "Revocatorias", segment: "revocation-requests" },
  // Pedido del usuario (2026-09-16) — modo Dashboard/QX, ventana de revocatoria (HU #12569) y
  // feature flags operativos: antes SOLO vivían en la ruta legacy `tramites`, que dejó de estar
  // enlazada desde cualquier menú cuando este hub se reorganizó (quedó huérfana, solo por URL).
  { id: "configuracion", label: "Configuración", segment: "configuracion" },
];

/** Keys del dock Admin OT (agrupación distinta a SuperAdmin). */
export const OT_ADM_DOCK = {
  rules: "ot-adm-rules",
  documents: "ot-adm-documents",
  requirements: "ot-adm-requirements",
  tramites: "ot-adm-tramites",
  usuarios: "ot-adm-usuarios",
  reportes: "ot-adm-reportes",
  mandatos: "ot-adm-mandatos",
  imprintValidation: "ot-adm-imprint-validation",
  configuracion: "ot-adm-configuracion",
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
