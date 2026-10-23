// Columnas configurables de la bandeja (spec §7.1, matriz T2): cada usuario oculta las que no usa y se recuerdan en
// este navegador. Se guardan las OCULTAS y las columnas que existían al guardar: una columna nueva aparece visible, salvo
// las que vienen ocultas por defecto. El número del comparendo nunca se oculta (es la fila). Lo visible es también lo
// que trae el Excel (HU #13871).

export const COLUMNA_FIJA = "numero";

export const claveColumnas = (usuarioId: string) => `gdc.bandeja.columnasOcultas.${usuarioId}`;

/** HU #13871: las columnas que antes solo estaban en el Excel; existen en la tabla, pero vienen ocultas. */
export const OCULTAS_POR_DEFECTO = ["ventana", "finDescuento", "activo", "cobroCoactivo"];

/**
 * Interpreta lo guardado: solo claves que existen y nunca la fija. Una columna de `porDefecto` queda oculta mientras el
 * usuario no la haya visto al guardar (las listas viejas, de antes de que existieran, no la conocían). Sin nada guardado
 * o con algo dañado: solo las ocultas por defecto.
 */
export function leerOcultas(texto: string | null | undefined, validas: readonly string[], porDefecto: readonly string[] = []): string[] {
  const limpiar = (v: unknown[]) => v.filter((x): x is string => typeof x === "string" && x !== COLUMNA_FIJA && validas.includes(x));
  const nuevas = (conocidas: string[]) => porDefecto.filter((k) => validas.includes(k) && !conocidas.includes(k));
  if (!texto) return nuevas([]);
  try {
    const v: unknown = JSON.parse(texto);
    if (Array.isArray(v)) return [...new Set([...limpiar(v), ...nuevas([])])];
    if (v && typeof v === "object" && Array.isArray((v as { ocultas?: unknown }).ocultas)) {
      const { ocultas, conocidas } = v as { ocultas: unknown[]; conocidas?: unknown };
      return [...new Set([...limpiar(ocultas), ...nuevas(Array.isArray(conocidas) ? limpiar(conocidas) : [])])];
    }
    return nuevas([]);
  } catch {
    return nuevas([]);
  }
}

/** De las claves que el usuario dejó visibles, las que quedan ocultas. */
export const ocultasDesde = (visibles: readonly string[], todas: readonly string[]) =>
  todas.filter((k) => k !== COLUMNA_FIJA && !visibles.includes(k));

export function guardadas(usuarioId: string, validas: readonly string[]): string[] {
  if (typeof window === "undefined") return leerOcultas(null, validas, OCULTAS_POR_DEFECTO);
  try {
    return leerOcultas(window.localStorage.getItem(claveColumnas(usuarioId)), validas, OCULTAS_POR_DEFECTO);
  } catch {
    return leerOcultas(null, validas, OCULTAS_POR_DEFECTO);
  }
}

export function guardar(usuarioId: string, ocultas: readonly string[], conocidas: readonly string[]) {
  try {
    window.localStorage.setItem(claveColumnas(usuarioId), JSON.stringify({ ocultas, conocidas }));
  } catch {
    // Sin almacenamiento (modo privado): la preferencia dura lo que dure la pantalla.
  }
}
