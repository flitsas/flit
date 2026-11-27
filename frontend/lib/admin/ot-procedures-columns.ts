// Columnas de la tabla de trámites del OT (ClientProceduresTable).

import { COPY } from "@/lib/copy/copy-catalog";

export interface OtProceduresColumnDef {
  key: string;
  label: string;
  /** Si true, la cabecera es clickable para ordenar (sortBy = key). */
  sortable?: boolean;
  /**
   * Por qué ordena la columna, cuando NO coincide con su rótulo. Solo lo necesita la celda
   * empresa+gestor (layout RN-07, A06): muestra dos datos pero el API únicamente sabe ordenar
   * por el gestor, y anunciar un orden por empresa+persona prometería un orden que no existe.
   */
  sortLabel?: string;
  /**
   * HU #14744 — columna que SOLO existe en las pestañas «Todos» y «Matrículas» (no en Traspaso ni Otros).
   * Fuera de ella no se pinta ni se ofrece en «Columnas», sin tocar la preferencia guardada. Nunca
   * va en el default.
   */
  soloTodosYMatriculas?: boolean;
}

export const OT_PROCEDURES_COLUMNS: readonly OtProceduresColumnDef[] = [
  { key: "radicado", label: COPY.B15, sortable: true },
  { key: "vin", label: COPY.A02Vin, sortable: true },
  { key: "placa", label: COPY.A02Placa, sortable: true },
  { key: "vendedor", label: COPY.A01, sortable: true },
  { key: "comprador", label: COPY.B17, sortable: true },
  // Ordenable desde la HU #12217: el backend ya resuelve el ORDER BY por nombre del tipo.
  { key: "tipoTramite", label: COPY.A03, sortable: true },
  // Empresa y gestor en UNA celda (layout RN-07). El vocablo de la persona es A06 Gestor.
  { key: "empresaGestor", label: COPY.A06, sortable: true, sortLabel: COPY.A06 },
  { key: "estado", label: COPY.B18, sortable: true },
  { key: "fechaRadicacion", label: COPY.A04, sortable: true },
  { key: "digitoPlaca", label: "Dígito de placa", soloTodosYMatriculas: true },
] as const;

/** Todas las columnas visibles por defecto, salvo las de matrícula inicial (HU #14744: se marcan a mano). */
export const DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS: readonly string[] = OT_PROCEDURES_COLUMNS.filter(
  (c) => !c.soloTodosYMatriculas,
).map((c) => c.key);

/** HU #14744 — claves que solo existen en «Todos» y «Matrículas». */
export const OT_TODOS_Y_MATRICULAS_KEYS: readonly string[] = OT_PROCEDURES_COLUMNS.filter(
  (c) => c.soloTodosYMatriculas,
).map((c) => c.key);

/** HU #14744 — quita (sin tocar la preferencia) esas columnas fuera de «Todos» y «Matrículas». */
export function applyOtTodosYMatriculasColumns(visible: readonly string[], enAlcance: boolean): string[] {
  return enAlcance ? [...visible] : visible.filter((k) => !OT_TODOS_Y_MATRICULAS_KEYS.includes(k));
}

/** HU #14744 — catálogo del selector «Columnas» según la pestaña. */
export function otSelectableColumns(enAlcance: boolean): readonly OtProceduresColumnDef[] {
  return enAlcance ? OT_PROCEDURES_COLUMNS : OT_PROCEDURES_COLUMNS.filter((c) => !c.soloTodosYMatriculas);
}

/** Una opción de orden ofrecida por la cabecera de una columna (HU #12219). */
export interface OtProceduresSortOption {
  id: string;
  label: string;
  /** Clave de orden del API. */
  sort: string;
  kind: "texto" | "fecha";
}

/**
 * Por cuáles de sus datos se puede ordenar una columna (HU #12219).
 *
 * <p>La celda empresa+gestor (A06) apila DOS datos y un clic en la cabecera no puede decir por
 * cuál ordena: hasta ahora ordenaba siempre por el gestor, así que la cabecera prometía un orden
 * por empresa que no existía. Devolviendo la lista, la cabecera ofrece un desplegable cuando hay
 * más de un dato y conserva el clic simple cuando hay uno solo.</p>
 */
export function otProceduresSortOptions(columnKey: string): OtProceduresSortOption[] {
  switch (columnKey) {
    case "empresaGestor":
      return [
        { id: "empresa", label: "Empresa cliente", sort: "empresa", kind: "texto" },
        { id: "gestor", label: COPY.A06, sort: "gestor", kind: "texto" },
      ];
    default:
      return [];
  }
}

/** Mapea la clave de columna UI → sortBy del API. */
export function otColumnToSortBy(columnKey: string): string {
  switch (columnKey) {
    case "radicado":
      return "radicado";
    case "vin":
      return "vin";
    case "placa":
      return "placa";
    case "vendedor":
      return "vendedor";
    case "comprador":
      return "comprador";
    case "empresaGestor":
      return "gestor";
    case "tipoTramite":
      return "tipo_tramite";
    case "estado":
      return "estado";
    case "fechaRadicacion":
      return "createdAt";
    default:
      return "createdAt";
  }
}
