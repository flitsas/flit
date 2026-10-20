"use client";

// Movido a @flit/ui (B-02). Este archivo reexporta la tabla compartida y le suma, en la carga, el
// loader del carrito del modelo único de tabla (Bug #13055): @flit/ui no puede importar el carrito,
// que vive en la app.
import { DataTable as DataTableBase, type DataTableProps } from "@flit/ui/DataTable";
import { CarLoaderModal } from "@/components/atom/CarLoader";

export type {
  CellAlign,
  DataTableColumn,
  DataTablePagination,
  DataTableProps,
} from "@flit/ui/DataTable";

export function DataTable<T>(props: DataTableProps<T>) {
  if (props.status === "loading") return <CarLoaderModal label="Cargando…" />;
  return <DataTableBase {...props} />;
}
