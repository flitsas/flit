// Buscador Ctrl K de la suite (HU #14480). Lógica pura: qué entradas existen y cuáles coinciden con lo escrito.
// Las secciones salen SOLO de los grupos del dock ya filtrados por permiso (`buildDock`), así que el buscador nunca
// ofrece algo que el rol no ve en el dock. Las acciones y la placa las declara cada producto, siempre como una ruta que
// ya existe, y cada una nombra la entrada del dock de la que depende.
import type { DockEntry, DockGroup, NavIcon } from "./nav";

/** Acción rápida que declara un producto: una ruta (o parámetro) que ya existe. */
export interface SearchAction {
  key: string;
  label: string;
  /** Qué hace, en una línea. */
  hint?: string;
  href: string;
  /** Clave de la entrada del dock que tiene que estar visible para ofrecer la acción. */
  requires: string;
}

/** Búsqueda de placa que declara un producto: a dónde lleva una placa y de qué entrada del dock depende. */
export interface PlateSearch {
  href: (placa: string) => string;
  /** Texto del resultado, p. ej. «Buscar en Trámites». */
  hint: string;
  requires: string;
}

export type SearchGroupId = "secciones" | "acciones" | "placas";

export const SEARCH_GROUP_LABEL: Record<SearchGroupId, string> = {
  secciones: "Secciones",
  acciones: "Acciones",
  placas: "Placas",
};

export interface SearchResult {
  id: string;
  group: SearchGroupId;
  label: string;
  description: string;
  href: string;
  icon?: NavIcon;
  /** Ilustración de la propuesta (la misma del dock); sin ella se pinta el ícono de línea. */
  ilustracion?: string;
}

/** Íconos fijos del buscador: el disco del kit (`public/assets/kit/modulo` existe en las 4 apps). */
const ILUS_INICIO = "/assets/kit/modulo/inicio.svg";
const ILUS_PLACA = "/assets/kit/modulo/consulta-placa.svg";

/** Placa colombiana: carro (ABC123) o moto (ABC12D). Se compara sin espacios ni guiones y en mayúsculas. */
const PLACA = /^[A-Z]{3}\d{2}[A-Z0-9]$/;

export function normalizePlate(text: string): string {
  return text.toUpperCase().replace(/[\s-]/g, "");
}

export function looksLikePlate(text: string): boolean {
  return PLACA.test(normalizePlate(text));
}

/** Minúsculas y sin tildes, para que «tramites» encuentre «Trámites». */
export function normalizeText(text: string): string {
  return text
    .toLowerCase()
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .trim();
}

/** Las claves de todas las entradas visibles del dock (también las de los submenús). */
export function visibleKeys(groups: DockGroup[]): Set<string> {
  const keys = new Set<string>();
  const add = (e: DockEntry) => {
    keys.add(e.key);
    e.children?.forEach(add);
  };
  groups.forEach((g) => g.items.forEach(add));
  return keys;
}

/** Secciones navegables: Inicio y cada entrada del dock con ruta (un contenedor `href: ""` aporta solo sus hijos). */
export function sectionResults(groups: DockGroup[], home?: { href: string; label: string }): SearchResult[] {
  const out: SearchResult[] = [];
  if (home) out.push({ id: "sec:inicio", group: "secciones", label: "Inicio", description: home.label, href: home.href, ilustracion: ILUS_INICIO });
  const add = (e: DockEntry, trail: string) => {
    if (e.href) out.push({ id: `sec:${e.key}`, group: "secciones", label: e.label, description: trail, href: e.href, icon: e.icon, ilustracion: e.ilustracion });
    e.children?.forEach((c) => add(c, `${trail} · ${e.label}`));
  };
  groups.forEach((g) => g.items.forEach((e) => add(e, g.label)));
  return out;
}

export function buildSearchResults(
  query: string,
  groups: DockGroup[],
  options: { home?: { href: string; label: string }; actions?: SearchAction[]; plate?: PlateSearch } = {},
): SearchResult[] {
  const keys = visibleKeys(groups);
  // La acción toma la ilustración de la sección que la habilita (p. ej. «Nuevo trámite» → la de Trámites).
  const ilusPorClave = new Map<string, string>();
  const guardar = (e: DockEntry) => {
    if (e.ilustracion) ilusPorClave.set(e.key, e.ilustracion);
    e.children?.forEach(guardar);
  };
  groups.forEach((g) => g.items.forEach(guardar));
  const q = normalizeText(query);
  const matches = (r: SearchResult) => !q || normalizeText(`${r.label} ${r.description}`).includes(q);

  const sections = sectionResults(groups, options.home).filter(matches);
  const actions = (options.actions ?? [])
    .filter((a) => keys.has(a.requires))
    .map<SearchResult>((a) => ({ id: `acc:${a.key}`, group: "acciones", label: a.label, description: a.hint ?? "", href: a.href, ilustracion: ilusPorClave.get(a.requires) }))
    .filter(matches);

  const plates: SearchResult[] = [];
  const plate = options.plate;
  if (plate && keys.has(plate.requires) && looksLikePlate(query)) {
    const placa = normalizePlate(query);
    plates.push({ id: `pla:${placa}`, group: "placas", label: placa, description: plate.hint, href: plate.href(placa), ilustracion: ILUS_PLACA });
  }

  // Con una placa escrita, la placa va primero: es lo que se buscaba.
  return [...plates, ...sections, ...actions];
}
