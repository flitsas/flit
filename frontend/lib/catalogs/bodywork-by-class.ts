/**
 * Catálogo de carrocerías permitidas por clase de vehículo (RUNT).
 * Fuente: carroceria.xlsx — generado automáticamente.
 * Regla: al activar transformación de carrocería, el selector solo lista
 * opciones de `byVehicleClass[claseRunt]`.
 */
import catalog from './bodywork-by-class.json';

export type BodyworkOption = { code: string; name: string };

export const BODYWORK_VEHICLE_CLASSES = catalog.vehicleClasses as string[];

export const BODYWORK_BY_VEHICLE_CLASS = catalog.byVehicleClass as Record<
  string,
  BodyworkOption[]
>;

/**
 * Normaliza clase RUNT: trim + upper + sin tildes/diacríticos + espacios colapsados.
 * El RUNT real puede devolver la clase acentuada (p. ej. "CAMIÓN") según el
 * proveedor/mapeador que responda (Bug #11629); el catálogo generado desde
 * carroceria.xlsx guarda las 18 claves sin tilde.
 */
export function normalizeVehicleClass(raw: string | null | undefined): string {
  return (raw ?? '')
    .trim()
    .toUpperCase()
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/\s+/g, ' ');
}

/**
 * Variantes ortográficas reales (no de tildes) entre la clase normalizada y la
 * clave del catálogo. Ejemplo confirmado: el RUNT usa "SEMIRREMOLQUE" (doble
 * R, ortografía estándar) mientras carroceria.xlsx generó la clave
 * "SEMIREMOLQUE" (una sola R). No es un caso cubierto por quitar tildes, así
 * que se mapea explícitamente en vez de aplicar una heurística genérica de
 * "colapsar consonantes dobles" que podría confundir clases distintas.
 */
const VEHICLE_CLASS_SPELLING_ALIASES: Record<string, string> = {
  SEMIRREMOLQUE: 'SEMIREMOLQUE',
};

/**
 * Índice de claves del catálogo, derivado (no mantenido a mano) normalizando
 * cada clave real de `byVehicleClass`. Así el lookup sigue funcionando aunque
 * el .json se regenere y cambien mayúsculas/tildes en las claves de origen.
 */
const NORMALIZED_VEHICLE_CLASS_INDEX: Record<string, string> = Object.keys(
  BODYWORK_BY_VEHICLE_CLASS,
).reduce<Record<string, string>>((index, originalKey) => {
  index[normalizeVehicleClass(originalKey)] = originalKey;
  return index;
}, {});

/**
 * Resuelve la clave normalizada de entrada a la clave real del catálogo,
 * aplicando primero el índice derivado y, si no hay match, el alias
 * ortográfico conocido.
 */
function resolveVehicleClassKey(normalizedKey: string): string | null {
  if (!normalizedKey) return null;
  if (NORMALIZED_VEHICLE_CLASS_INDEX[normalizedKey]) {
    return NORMALIZED_VEHICLE_CLASS_INDEX[normalizedKey];
  }
  const alias = VEHICLE_CLASS_SPELLING_ALIASES[normalizedKey];
  if (alias && NORMALIZED_VEHICLE_CLASS_INDEX[alias]) {
    return NORMALIZED_VEHICLE_CLASS_INDEX[alias];
  }
  // RUNT a veces manda la clase con calificativo ("CAMION CISTERNA"). Empata la clave
  // más larga del catálogo que coincida exacta o como prefijo de palabra; no "CAMION" dentro
  // de "CAMIONETA".
  const catalogKeys = Object.keys(NORMALIZED_VEHICLE_CLASS_INDEX).sort((a, b) => b.length - a.length);
  const hit = catalogKeys.find((k) => normalizedKey === k || normalizedKey.startsWith(`${k} `));
  return hit ? NORMALIZED_VEHICLE_CLASS_INDEX[hit] : null;
}

type BodyworkRow = BodyworkOption & { classes?: string[] };

const ALL_BODYWORK_ROWS = catalog.allBodyworks as BodyworkRow[];

/**
 * Carrocerías permitidas para la clase del vehículo consultado en RUNT.
 * Fuente canónica: `allBodyworks[].classes` (cada fila declara a qué clase pertenece).
 * Si no hay clase o no hay match, retorna lista vacía (no inventar ni listar el catálogo completo).
 */
export function getBodyworksForVehicleClass(
  vehicleClass: string | null | undefined,
): BodyworkOption[] {
  const key = normalizeVehicleClass(vehicleClass);
  if (!key) return [];
  const resolvedKey = resolveVehicleClassKey(key);
  if (!resolvedKey) {
    console.warn('[bodywork-by-class] clase de vehículo sin match en el catálogo', {
      raw: vehicleClass,
      normalized: key,
    });
    return [];
  }
  const want = normalizeVehicleClass(resolvedKey);
  const tagged = ALL_BODYWORK_ROWS.filter((row) =>
    (row.classes ?? []).some((c) => normalizeVehicleClass(c) === want),
  );
  const source = tagged.length > 0 ? tagged : (BODYWORK_BY_VEHICLE_CLASS[resolvedKey] ?? []);
  const seen = new Set<string>();
  const unique: BodyworkOption[] = [];
  for (const row of source) {
    const nameKey = row.name.trim().toUpperCase();
    if (!nameKey || seen.has(nameKey)) continue;
    seen.add(nameKey);
    unique.push({ code: row.code, name: row.name });
  }
  return unique;
}

export function findBodyworkName(
  codeOrName: string | null | undefined,
  vehicleClass?: string | null,
): string | null {
  const raw = (codeOrName ?? '').trim();
  if (!raw) return null;
  const options = vehicleClass
    ? getBodyworksForVehicleClass(vehicleClass)
    : (catalog.allBodyworks as BodyworkOption[]);
  const byCode = options.find((o) => o.code === raw);
  if (byCode) return byCode.name;
  const byName = options.find((o) => o.name.toUpperCase() === raw.toUpperCase());
  return byName?.name ?? raw;
}
