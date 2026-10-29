/** Municipios que el ciudadano puede elegir. La misma lista que usa el formulario de actores de FLIT 2. */
export const CIUDADES: readonly string[] = [
  "Bogota", "Medellin", "Cali", "Barranquilla", "Cartagena", "Bucaramanga",
  "Cucuta", "Pereira", "Manizales", "Santa Marta", "Ibague", "Villavicencio",
  "Pasto", "Monteria", "Neiva", "Armenia", "Valledupar", "Popayan", "Sincelejo",
  "Tunja", "Florencia", "Riohacha", "Quibdo", "Yopal", "Mocoa", "Leticia",
  "Arauca", "San Jose Del Guaviare", "Mitu", "Puerto Carreno", "Inirida",
  "Envigado", "Bello", "Itagui", "Soacha", "Soledad", "Floridablanca", "Palmira",
  "Buenaventura", "Barrancabermeja", "Dosquebradas", "Tulua", "Sogamoso",
  "Girardot", "Maicao", "Magangue", "Turbo", "Apartado", "Cartago", "Duitama",
  "Fusagasuga", "Girardota", "Zipaquira", "Facatativa", "Chia", "Rionegro",
  "Sabaneta", "La Estrella", "Copacabana", "Caldas", "Cajica", "Marinilla",
  "El Carmen De Viboral", "La Ceja", "Guatape", "El Retiro", "El Penol",
  "La Union", "Sonson", "Barbosa", "Buga", "Jamundi", "Yumbo", "Candelaria",
  "Florida", "Pradera", "El Cerrito", "Dagua", "Vijes", "La Cumbre", "Restrepo",
  "Caloto", "Santander De Quilichao", "Puerto Tejada", "Miranda", "Corinto",
  "Guachene", "Piendamo", "Silvia", "Toribio", "Suarez", "Buenos Aires",
  "Cajibio", "Timbio", "El Tambo", "Argelia", "Balboa", "Patia", "Mercaderes",
  "Bolivar", "San Sebastian", "La Vega", "Almaguer", "Rosas", "Sotara",
  "Purace", "Coconuco", "Totoro",
];

/** Nombre del catálogo si el texto coincide; si no, null. */
export function ciudadCanonica(valor: string): string | null {
  const q = valor.trim().toLowerCase();
  if (!q) return null;
  return CIUDADES.find((c) => c.toLowerCase() === q) ?? null;
}

/** Sugerencias a partir de 2 letras. No abre la lista completa. */
export function filtrarCiudades(consulta: string, minimo = 2, limite = 8): string[] {
  const q = consulta.trim().toLowerCase();
  if (q.length < minimo) return [];
  return CIUDADES.filter((c) => c.toLowerCase().includes(q)).slice(0, limite);
}
