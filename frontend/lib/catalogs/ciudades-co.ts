// Catálogo de ciudades de Colombia para el autocomplete de captura de actores.
// Portado de Johan (apps/web/src/constants/tramite.ts · CIUDADES_CO). Lista
// pragmática (no exhaustiva) de municipios frecuentes; suficiente para el typeahead.

export const CIUDADES_CO: readonly string[] = [
  'Bogota', 'Medellin', 'Cali', 'Barranquilla', 'Cartagena', 'Bucaramanga',
  'Cucuta', 'Pereira', 'Manizales', 'Santa Marta', 'Ibague', 'Villavicencio',
  'Pasto', 'Monteria', 'Neiva', 'Armenia', 'Valledupar', 'Popayan', 'Sincelejo',
  'Tunja', 'Florencia', 'Riohacha', 'Quibdo', 'Yopal', 'Mocoa', 'Leticia',
  'Arauca', 'San Jose Del Guaviare', 'Mitu', 'Puerto Carreno', 'Inirida',
  'Envigado', 'Bello', 'Itagui', 'Soacha', 'Soledad', 'Floridablanca', 'Palmira',
  'Buenaventura', 'Barrancabermeja', 'Dosquebradas', 'Tulua', 'Sogamoso',
  'Girardot', 'Maicao', 'Magangue', 'Turbo', 'Apartado', 'Cartago', 'Duitama',
  'Fusagasuga', 'Girardota', 'Zipaquira', 'Facatativa', 'Chia', 'Rionegro',
  'Sabaneta', 'La Estrella', 'Copacabana', 'Caldas', 'Cajica', 'Marinilla',
  'El Carmen De Viboral', 'La Ceja', 'Guatape', 'El Retiro', 'El Penol',
  'La Union', 'Sonson', 'Barbosa', 'Buga', 'Jamundi', 'Yumbo', 'Candelaria',
  'Florida', 'Pradera', 'El Cerrito', 'Dagua', 'Vijes', 'La Cumbre', 'Restrepo',
  'Caloto', 'Santander De Quilichao', 'Puerto Tejada', 'Miranda', 'Corinto',
  'Guachene', 'Piendamo', 'Silvia', 'Toribio', 'Suarez', 'Buenos Aires',
  'Cajibio', 'Timbio', 'El Tambo', 'Argelia', 'Balboa', 'Patia', 'Mercaderes',
  'Bolivar', 'San Sebastian', 'La Vega', 'Almaguer', 'Rosas', 'Sotara',
  'Purace', 'Coconuco', 'Totoro',
];

/**
 * Filtra ciudades por prefijo/substring (case-insensitive, sin tildes en el
 * catálogo). Devuelve [] si la consulta tiene menos de `minChars` caracteres,
 * para no abrir el dropdown con la lista completa.
 */
export function filterCiudades(query: string, minChars = 2, limit = 8): string[] {
  const q = query.trim().toLowerCase();
  if (q.length < minChars) return [];
  return CIUDADES_CO.filter((c) => c.toLowerCase().includes(q)).slice(0, limit);
}
