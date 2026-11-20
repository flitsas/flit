// Presentación de nombres de Persona Natural. Solo cambia cómo se ve el texto: los datos, la búsqueda y los filtros
// siguen usando el valor original (la búsqueda ya ignora mayúsculas y tildes).

const CONECTORES = new Set(["de", "del", "la", "las", "los", "el", "y", "e", "en", "a", "al"]);
const VOCALES = /[aeiouyáéíóúü]/i;
/** Siglas habituales del sector que llevan vocales y deben quedar en mayúscula. */
const SIGLAS = new Set(["TTO", "TTE", "DTT", "ITT", "OT", "UT", "SIMIT", "RUNT", "SAS", "ESP"]);

/**
 * «SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA» → «Secretaria Distrital de Movilidad de Bogota».
 * - Las palabras que ya traen minúsculas se dejan igual (el dato ya viene bien escrito o es una sigla mixta).
 * - Las siglas sin vocales («D.C.»), las del sector («TTO», «TTE») y las palabras con números se dejan en mayúscula.
 * - No se pueden recuperar las tildes que el dato no trae.
 */
export function nombrePropio(texto: string | null | undefined): string {
  if (!texto) return "";
  return texto
    .trim()
    .split(/\s+/)
    .map((palabra, i) => {
      if (/[a-záéíóúüñ]/.test(palabra)) return palabra;
      if (/\d/.test(palabra) || !VOCALES.test(palabra) || SIGLAS.has(palabra)) return palabra;
      const minuscula = palabra.toLowerCase();
      if (i > 0 && CONECTORES.has(minuscula)) return minuscula;
      return minuscula.charAt(0).toUpperCase() + minuscula.slice(1);
    })
    .join(" ");
}
