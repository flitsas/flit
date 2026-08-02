/** Rutas del hub consola OT (HU #10236). */
export type OtHubTabId =
  | "tramites"
  | "webhooks"
  | "client-procedures"
  | "rules"
  | "documents"
  | "requirements"
  | "plate-ranges"
  | "usuarios";

export interface OtHubTab {
  id: OtHubTabId;
  label: string;
  segment: OtHubTabId;
}

/**
 * Pestañas visibles del hub. "Trámites" y "Webhooks" quedaron fuera de la navegación: sus rutas
 * siguen respondiendo si se entra por URL, pero no se ofrecen en la consola. Al retirarse "Trámites"
 * el hub pasa a abrir en "Trámites clientes" (ver la página índice del hub).
 */
export const OT_HUB_TABS: OtHubTab[] = [
  { id: "client-procedures", label: "Trámites clientes", segment: "client-procedures" },
  { id: "rules", label: "Reglas", segment: "rules" },
  { id: "documents", label: "Documentos", segment: "documents" },
  { id: "requirements", label: "Requisitos", segment: "requirements" },
  { id: "plate-ranges", label: "Preasignación de placa", segment: "plate-ranges" },
  { id: "usuarios", label: "Usuarios", segment: "usuarios" },
  // HU #11202 (AC4) — la gestión de mandatarios salió del perfil del organismo: ahora la hace la
  // COMPAÑÍA desde su configurador, eligiendo en cuáles de sus organismos aplica cada mandatario.
];

export function otHubModulePath(transitOfficeId: string, tab: OtHubTabId): string {
  return `/admin/transit-offices/${transitOfficeId}/${tab}`;
}

export function otHubListPath(): string {
  return "/admin/transit-offices";
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
