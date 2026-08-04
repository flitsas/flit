/**
 * Catálogos de transformaciones de vehículo (A4/B4 · HU #10674 · ADR-0029).
 *
 * Colores: catálogo persistido en `catalogs.vehicle_colors` vía
 * `GET /api/v1/tramites/vehicle-colors` (VehicleColorSearchSelect). Ya no hay lista
 * placeholder en el front.
 *
 * Combustible: lista cerrada alineada con los checkboxes del FUR
 * (FurFieldMapper.MarkCombustible).
 */

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
