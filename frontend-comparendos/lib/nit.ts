/** Dígito de verificación DIAN (mismos pesos que `Nit.cs` en core-comparendos). */
const PESOS = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

/** Separa base y DV de «900.123.456-7» o «900123456». */
export function separarNit(texto: string | null | undefined): { base: string; dv: number | null } | null {
  if (!texto?.trim()) return null;
  const partes = texto.trim().split("-").map((p) => p.trim());
  if (partes.length > 2) return null;
  let base = partes[0].replace(/[.\s,]/g, "");
  if (!base || base.length > 15 || !/^\d+$/.test(base)) return null;
  base = base.replace(/^0+/, "");
  if (!base) return null;
  if (partes.length === 1) return { base, dv: null };
  if (partes[1].length !== 1 || !/\d/.test(partes[1])) return null;
  return { base, dv: Number(partes[1]) };
}

export function digitoVerificacion(baseNit: string): number {
  if (!baseNit || baseNit.length > PESOS.length || !/^\d+$/.test(baseNit)) {
    throw new Error("El NIT solo admite hasta 15 dígitos.");
  }
  let suma = 0;
  for (let i = 0; i < baseNit.length; i++) {
    suma += Number(baseNit[baseNit.length - 1 - i]) * PESOS[i];
  }
  const residuo = suma % 11;
  return residuo === 0 || residuo === 1 ? residuo : 11 - residuo;
}

/** true si el texto es NIT válido (con o sin DV; si trae DV, debe coincidir). */
export function nitValido(texto: string): boolean {
  const s = separarNit(texto);
  if (!s) return false;
  if (s.dv === null) return true;
  return digitoVerificacion(s.base) === s.dv;
}

/** Mensaje de error inline, o null si está vacío o válido. */
export function errorNit(texto: string): string | null {
  const t = texto.trim();
  if (!t) return null;
  const s = separarNit(t);
  if (!s) return "Usa el formato 900.123.456-7 (el dígito de verificación es opcional).";
  if (s.dv !== null && digitoVerificacion(s.base) !== s.dv) {
    return `El dígito de verificación debería ser ${digitoVerificacion(s.base)}.`;
  }
  return null;
}
