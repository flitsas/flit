/**
 * Lógica PURA de los filtros genéricos por columna (HU #13997, Feature #13996, Épica #13514).
 * Sin React: la usan `TablaFiltrosBar` / `useTablaFiltros` y los contenedores de cada tabla.
 *
 * Contrato de datos
 * -----------------
 * - `ColumnaFiltro`: lo que una tabla DECLARA de cada columna filtrable.
 * - `CondicionFiltro`: lo que el usuario APLICÓ (una por columna; unión discriminada por `tipo`).
 * - `filtrarFilas`: modo CLIENTE. Combina todo con AND.
 *     texto     -> «contiene», sin mayúsculas, tildes, puntos, guiones, guiones bajos ni espacios.
 *     seleccion -> el valor de la fila pertenece a `valores` (varias opciones = OR dentro de la columna).
 *     fecha     -> rango inclusivo en FECHA LOCAL (`yyyy-mm-dd`); nunca `toISOString()`.
 * - `condicionesAParams`: modo SERVIDOR. Convierte lo aplicado en `{ [param]: valor }`.
 *
 * Ver `components/atom/TablaFiltrosBar.tsx` para los ejemplos de uso completos (cliente y servidor).
 */

export type TipoColumnaFiltro = "texto" | "seleccion" | "fecha";

export interface OpcionFiltro {
  value: string;
  label: string;
}

export interface ColumnaFiltro {
  /** Identificador estable; es la clave de `accesores.columnas` y el `columnaId` de la condición. */
  id: string;
  /** Rótulo visible en la lista de «+ Filtro» y en el editor. */
  label: string;
  tipo: TipoColumnaFiltro;
  /** Obligatorio para `seleccion`. */
  opciones?: readonly OpcionFiltro[];
  /** Modo servidor: nombre del query param (por defecto, `id`). */
  param?: string;
  /** Modo servidor, `fecha`: params del rango (por defecto `${param}From` / `${param}To`). */
  paramDesde?: string;
  paramHasta?: string;
}

export type CondicionFiltro =
  | { columnaId: string; tipo: "texto"; texto: string }
  | { columnaId: string; tipo: "seleccion"; valores: string[] }
  | { columnaId: string; tipo: "fecha"; desde: string; hasta: string };

export type ValorCelda = string | number | Date | null | undefined;

export interface AccesoresFiltro<T> {
  /** Valor de cada columna filtrable. Para `seleccion`, una fila puede devolver varios valores. */
  columnas: Record<string, (fila: T) => ValorCelda | readonly ValorCelda[]>;
  /** Textos sobre los que busca el buscador (cualquiera que contenga el término sirve). */
  busqueda?: (fila: T) => readonly ValorCelda[];
}

/** Quita mayúsculas, tildes, puntos, guiones, guiones bajos y espacios. */
export function normalizarTexto(v: unknown): string {
  return String(v ?? "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[\s.\-_]+/g, "");
}

const FECHA_SOLO_DIA = /^\d{4}-\d{2}-\d{2}$/;

function dosDigitos(n: number): string {
  return String(n).padStart(2, "0");
}

/** `yyyy-mm-dd` en hora LOCAL (sin `toISOString`, que desplaza por zona horaria). */
export function aFechaLocal(v: ValorCelda): string | null {
  if (v === null || v === undefined || v === "") return null;
  if (typeof v === "string" && FECHA_SOLO_DIA.test(v)) return v;
  const d = v instanceof Date ? v : new Date(v);
  if (Number.isNaN(d.getTime())) return null;
  return `${d.getFullYear()}-${dosDigitos(d.getMonth() + 1)}-${dosDigitos(d.getDate())}`;
}

/** ¿La condición aporta algo? Las vacías nunca se agregan ni se aplican. */
export function condicionEsValida(c: CondicionFiltro): boolean {
  switch (c.tipo) {
    case "texto":
      return c.texto.trim() !== "";
    case "seleccion":
      return c.valores.length > 0;
    case "fecha":
      return c.desde !== "" || c.hasta !== "";
  }
}

function aLista(v: ValorCelda | readonly ValorCelda[]): ValorCelda[] {
  return Array.isArray(v) ? [...(v as readonly ValorCelda[])] : [v as ValorCelda];
}

function cumple<T>(fila: T, c: CondicionFiltro, accesores: AccesoresFiltro<T>): boolean {
  const acceso = accesores.columnas[c.columnaId];
  // Columna sin accesor: no se puede evaluar, no se excluye nada.
  if (!acceso) return true;
  const valores = aLista(acceso(fila));
  switch (c.tipo) {
    case "texto": {
      const buscado = normalizarTexto(c.texto);
      return valores.some((v) => normalizarTexto(v).includes(buscado));
    }
    case "seleccion":
      return valores.some((v) => v !== null && v !== undefined && c.valores.includes(String(v)));
    case "fecha": {
      return valores.some((v) => {
        const dia = aFechaLocal(v);
        if (!dia) return false;
        if (c.desde && dia < c.desde) return false;
        if (c.hasta && dia > c.hasta) return false;
        return true;
      });
    }
  }
}

/** Modo cliente: filas que cumplen TODAS las condiciones y el buscador (AND). */
export function filtrarFilas<T>(
  filas: readonly T[],
  condiciones: readonly CondicionFiltro[],
  busqueda: string,
  accesores: AccesoresFiltro<T>,
): T[] {
  const validas = condiciones.filter(condicionEsValida);
  const termino = normalizarTexto(busqueda);
  if (validas.length === 0 && termino === "") return [...filas];
  return filas.filter((fila) => {
    if (termino !== "" && accesores.busqueda) {
      const hit = accesores.busqueda(fila).some((v) => normalizarTexto(v).includes(termino));
      if (!hit) return false;
    }
    return validas.every((c) => cumple(fila, c, accesores));
  });
}

export interface OpcionesParams {
  /** Nombre del param del buscador (por defecto `search`). */
  busquedaParam?: string;
}

/**
 * Modo servidor: `{ [param]: valor }` de lo aplicado. Texto -> valor; selección -> valores unidos por
 * coma; fecha -> `${param}From` / `${param}To` (o `paramDesde` / `paramHasta` de la columna).
 * Omite lo vacío, así que el resultado se puede esparcir directo en los query params.
 */
export function condicionesAParams(
  condiciones: readonly CondicionFiltro[],
  columnas: readonly ColumnaFiltro[],
  busqueda = "",
  opciones: OpcionesParams = {},
): Record<string, string> {
  const params: Record<string, string> = {};
  const termino = busqueda.trim();
  if (termino) params[opciones.busquedaParam ?? "search"] = termino;
  for (const c of condiciones) {
    if (!condicionEsValida(c)) continue;
    const col = columnas.find((x) => x.id === c.columnaId);
    if (!col) continue;
    const param = col.param ?? col.id;
    if (c.tipo === "texto") params[param] = c.texto.trim();
    else if (c.tipo === "seleccion") params[param] = c.valores.join(",");
    else {
      if (c.desde) params[col.paramDesde ?? `${param}From`] = c.desde;
      if (c.hasta) params[col.paramHasta ?? `${param}To`] = c.hasta;
    }
  }
  return params;
}
