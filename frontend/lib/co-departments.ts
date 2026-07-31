// Mapa código DANE → nombre de departamento de Colombia. El catálogo de organismos de
// tránsito guarda `departmentCode` como el código DANE de dos dígitos (p. ej. "05"), que por
// sí solo no le dice nada al administrador. Este mapa lo traduce a un nombre legible para
// pintarlo en el listado y ofrecer un filtro por departamento.
//
// Es un catálogo estable y cerrado (los 32 departamentos + Bogotá D.C.), por eso vive en el
// front como constante y no como una llamada más a la API.
const CO_DEPARTMENTS: Readonly<Record<string, string>> = {
  "05": "Antioquia",
  "08": "Atlántico",
  "11": "Bogotá D.C.",
  "13": "Bolívar",
  "15": "Boyacá",
  "17": "Caldas",
  "18": "Caquetá",
  "19": "Cauca",
  "20": "Cesar",
  "23": "Córdoba",
  "25": "Cundinamarca",
  "27": "Chocó",
  "41": "Huila",
  "44": "La Guajira",
  "47": "Magdalena",
  "50": "Meta",
  "52": "Nariño",
  "54": "Norte de Santander",
  "63": "Quindío",
  "66": "Risaralda",
  "68": "Santander",
  "70": "Sucre",
  "73": "Tolima",
  "76": "Valle del Cauca",
  "81": "Arauca",
  "85": "Casanare",
  "86": "Putumayo",
  "88": "San Andrés y Providencia",
  "91": "Amazonas",
  "94": "Guainía",
  "95": "Guaviare",
  "97": "Vaupés",
  "99": "Vichada",
};

/**
 * Nombre del departamento a partir del código DANE. Normaliza a dos dígitos (por si llega "5"
 * en vez de "05"). Si el código no está en el catálogo, devuelve el propio código para no
 * ocultar el dato — nunca cadena vacía.
 */
export function departmentName(code: string | null | undefined): string {
  if (!code) {
    return "—";
  }
  const key = code.padStart(2, "0");
  return CO_DEPARTMENTS[key] ?? code;
}
