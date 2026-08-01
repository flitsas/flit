// Columnas de la tabla de trámites del OT (ClientProceduresTable).

export interface OtProceduresColumnDef {
  key: string;
  label: string;
  /** Si true, la cabecera es clickable para ordenar (sortBy = key). */
  sortable?: boolean;
}

export const OT_PROCEDURES_COLUMNS: readonly OtProceduresColumnDef[] = [
  { key: "radicado", label: "Radicado", sortable: true },
  { key: "vin", label: "VIN", sortable: true },
  { key: "placa", label: "Placa", sortable: true },
  { key: "vendedor", label: "Propietario / vendedor", sortable: true },
  { key: "comprador", label: "Comprador", sortable: true },
  { key: "gestor", label: "Gestor", sortable: true },
  { key: "tipoTramite", label: "Tipo trámite" },
  { key: "empresaCliente", label: "Empresa cliente" },
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
    case "gestor":
      return "gestor";
    case "estado":
      return "estado";
    case "fechaRadicacion":
      return "createdAt";
    default:
      return "createdAt";
  }
}
