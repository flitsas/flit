/** Rutas del hub consola OT (HU #10236). */
export type OtHubTabId =
  | "tramites"
  | "webhooks"
  | "client-procedures"
  | "rules"
  | "documents";

export interface OtHubTab {
  id: OtHubTabId;
  label: string;
  segment: OtHubTabId;
}

export const OT_HUB_TABS: OtHubTab[] = [
  { id: "tramites", label: "Trámites", segment: "tramites" },
  { id: "webhooks", label: "Webhooks", segment: "webhooks" },
  { id: "client-procedures", label: "Trámites clientes", segment: "client-procedures" },
  { id: "rules", label: "Reglas", segment: "rules" },
  { id: "documents", label: "Documentos", segment: "documents" },
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
