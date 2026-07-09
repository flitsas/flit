/**
 * Catálogos PLACEHOLDER de transformaciones de vehículo (A4/B4 · HU #10674 · ADR-0029).
 *
 * Deuda explícita: listas cerradas provisionales en código. Se sustituyen cuando negocio
 * entregue el catálogo real de colores y combustibles del RUNT. Los valores de combustible
 * se mantienen alineados con los checkboxes del FUR (FurFieldMapper.MarkCombustible) para
 * que la transformación declarada se marque correctamente en el formulario.
 */

export const VEHICLE_COLOR_CATALOG: readonly string[] = [
  'BLANCO',
  'NEGRO',
  'GRIS',
  'PLATA',
  'ROJO',
  'AZUL',
  'VERDE',
  'AMARILLO',
  'NARANJA',
  'CAFÉ',
  'BEIGE',
  'DORADO',
  'VINOTINTO',
  'OTRO',
];

export const VEHICLE_FUEL_CATALOG: readonly string[] = [
  'GASOLINA',
  'DIESEL',
  'GAS NATURAL',
  'ELECTRICO',
  'HIBRIDO',
  'HIDROGENO',
  'ETANOL',
  'BIODIESEL',
  'MIXTO',
  'OTRO',
];
