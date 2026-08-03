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

/** Normaliza clase RUNT (trim + upper) para lookup. */
export function normalizeVehicleClass(raw: string | null | undefined): string {
  return (raw ?? '').trim().toUpperCase();
}

/**
 * Carrocerías permitidas para la clase del vehículo consultado en RUNT.
 * Si no hay clase o no hay match, retorna lista vacía (no inventar opciones).
 */
export function getBodyworksForVehicleClass(
  vehicleClass: string | null | undefined,
): BodyworkOption[] {
  const key = normalizeVehicleClass(vehicleClass);
  if (!key) return [];
  return BODYWORK_BY_VEHICLE_CLASS[key] ?? [];
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
