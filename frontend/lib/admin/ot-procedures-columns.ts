// Columnas de la tabla de trámites del OT (ClientProceduresTable).

export interface OtProceduresColumnDef {
  key: string;
  label: string;
  /** Si true, la cabecera es clickable para ordenar (sortBy = key). */
  sortable?: boolean;
  /**
   * Por qué ordena la columna, cuando NO coincide con su rótulo. Solo lo necesita "Empresa /
   * Gestor": muestra dos datos pero el API únicamente sabe ordenar por el gestor, y anunciar
   * "Ordenar por Empresa / Gestor" prometería un orden que no existe.
   */
  sortLabel?: string;
}

export const OT_PROCEDURES_COLUMNS: readonly OtProceduresColumnDef[] = [
  { key: "radicado", label: "Radicado", sortable: true },
  { key: "vin", label: "VIN", sortable: true },
  { key: "placa", label: "Placa", sortable: true },
  { key: "vendedor", label: "Propietario / vendedor", sortable: true },
  { key: "comprador", label: "Comprador", sortable: true },
  { key: "tipoTramite", label: "Tipo trámite" },
  // Empresa y gestor en UNA celda: los dos identifican a quien radicó el trámite, y separados
  // obligaban a barrer la fila de lado a lado para saber de dónde venía. Ordena por GESTOR, que es
  // lo único de los dos que el API sabe ordenar; el control de orden lo dice en su nombre.
  { key: "empresaGestor", label: "Empresa / Gestor", sortable: true, sortLabel: "gestor" },
  { key: "estado", label: "Estado", sortable: true },
  { key: "fechaRadicacion", label: "Fecha radicación", sortable: true },
] as const;

/** Todas las columnas visibles por defecto. */
export const DEFAULT_OT_PROCEDURES_VISIBLE_COLUMNS: readonly string[] = OT_PROCEDURES_COLUMNS.map(
  (c) => c.key,
);

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
    case "estado":
      return "estado";
    case "fechaRadicacion":
      return "createdAt";
    default:
      return "createdAt";
  }
}
