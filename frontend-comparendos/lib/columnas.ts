// Columnas configurables de la bandeja (spec §7.1, matriz T2): cada usuario oculta las que no usa y se recuerdan en
// este navegador. Se guardan las OCULTAS, así una columna nueva aparece visible sin tocar las preferencias. El número
// del comparendo nunca se oculta (es la fila).

export const COLUMNA_FIJA = "numero";

export const claveColumnas = (usuarioId: string) => `gdc.bandeja.columnasOcultas.${usuarioId}`;

/** Interpreta lo guardado: solo claves que existen y nunca la fija. Cualquier cosa rara = ninguna oculta. */
export function leerOcultas(texto: string | null | undefined, validas: readonly string[]): string[] {
  if (!texto) return [];
  try {
    const v: unknown = JSON.parse(texto);
    if (!Array.isArray(v)) return [];
    return v.filter((x): x is string => typeof x === "string" && x !== COLUMNA_FIJA && validas.includes(x));
  } catch {
    return [];
  }
}

/** De las claves que el usuario dejó visibles, las que quedan ocultas. */
export const ocultasDesde = (visibles: readonly string[], todas: readonly string[]) =>
  todas.filter((k) => k !== COLUMNA_FIJA && !visibles.includes(k));

export function guardadas(usuarioId: string, validas: readonly string[]): string[] {
  if (typeof window === "undefined") return [];
  try {
    return leerOcultas(window.localStorage.getItem(claveColumnas(usuarioId)), validas);
  } catch {
    return [];
  }
}

export function guardar(usuarioId: string, ocultas: readonly string[]) {
  try {
    window.localStorage.setItem(claveColumnas(usuarioId), JSON.stringify(ocultas));
  } catch {
    // Sin almacenamiento (modo privado): la preferencia dura lo que dure la pantalla.
  }
}
