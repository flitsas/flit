"use client";

import type { ReactNode } from "react";
import { DataTable, type DataTableColumn } from "@flit/ui/DataTable";
import { ETIQUETA_PUNTO_CLS, etiquetaKit } from "@flit/ui/StatusBadge";
import { formatoEntero, formatoFechaHora, formatoMonto, MONTO_SIN_DATO } from "@/lib/estadisticas/formato";
import { COLUMNAS_FECHA, COLUMNAS_MONTO, rotuloDeColumna, type ReporteCatalogo } from "@/lib/reportes/catalogo";
import { COLUMNA_EMPRESA, columnasVisibles, type FilaReporte, type PaginaReporte } from "@/lib/reportes/api";

const NUMERICAS: ReadonlySet<string> = new Set(["totalItems", "exitosas", "conError", "pendientes", ...COLUMNAS_MONTO]);
const TEXTO_LARGO_CLS = "block max-w-[16rem] truncate";

/** Una celda según su columna (spec-visual §0.1): montos «Sin dato» si son null, fechas en hora de Bogotá, enums con su etiqueta. */
function celda(reporte: ReporteCatalogo, clave: string, valor: unknown): ReactNode {
  if (clave === COLUMNA_EMPRESA.clave) {
    // row.empresa (Referencia, delta 11): el nombre; si llega vacío (compañía suspendida), el id.
    const empresa = valor && typeof valor === "object" ? (valor as { id?: unknown; nombre?: unknown }) : null;
    const texto = (typeof empresa?.nombre === "string" && empresa.nombre.trim()) || (typeof empresa?.id === "string" && empresa.id) || "";
    return texto ? (
      <span className={TEXTO_LARGO_CLS} title={texto}>
        {texto}
      </span>
    ) : (
      "—"
    );
  }
  if (COLUMNAS_MONTO.has(clave)) {
    return typeof valor === "number" ? (
      <span className="tabular-nums">{formatoMonto(valor)}</span>
    ) : (
      <span className="text-[color:var(--di-text-tertiary)]">{MONTO_SIN_DATO}</span>
    );
  }
  if (valor === null || valor === undefined || valor === "") return "—";
  if (COLUMNAS_FECHA.has(clave) && typeof valor === "string") return <time dateTime={valor}>{formatoFechaHora(valor)}</time>;
  if (clave === "parcial") {
    if (valor !== true) return "—";
    const kit = etiquetaKit("purple");
    return (
      <span className={kit.className} style={kit.style}>
        <span aria-hidden="true" className={ETIQUETA_PUNTO_CLS} />
        Parcial
      </span>
    );
  }
  if (typeof valor === "string") {
    const rotulo = rotuloDeColumna(reporte.tipo, clave, valor);
    if (rotulo) return rotulo;
    if (clave === "placa") return <span className="font-semibold uppercase tracking-[0.08em]">{valor}</span>;
    return (
      <span className={TEXTO_LARGO_CLS} title={valor}>
        {valor}
      </span>
    );
  }
  if (typeof valor === "number") return <span className="tabular-nums">{formatoEntero(valor)}</span>;
  if (Array.isArray(valor)) return valor.length ? valor.map(String).join(", ") : "—";
  if (typeof valor === "object" && "nombre" in (valor as Record<string, unknown>)) return String((valor as { nombre: unknown }).nombre ?? "—");
  return String(valor);
}

/**
 * Tabla de un reporte (spec-visual §3.7): `DataTable` del kit con las columnas en el orden y con la etiqueta que manda la
 * API (`columnas`), y `Pagination`. En la vista global del Super Admin la primera columna es «Compañía» (row.empresa.nombre,
 * delta 11); con una compañía elegida o para un usuario de compañía no se pinta. Las celdas largas se recortan con `title`.
 */
export function TablaReporte({
  reporte,
  pagina,
  vistaGlobal = false,
  onPagina,
}: {
  reporte: ReporteCatalogo;
  pagina: PaginaReporte;
  /** Super Admin sin ?empresa=: lleva la columna «Compañía». */
  vistaGlobal?: boolean;
  onPagina: (page: number) => void;
}) {
  type Fila = { fila: FilaReporte; i: number };
  const columnas: DataTableColumn<Fila>[] = columnasVisibles(pagina.columnas ?? [], vistaGlobal).map((c) => ({
    key: c.clave,
    header: c.etiqueta,
    align: NUMERICAS.has(c.clave) ? "right" : "left",
    render: ({ fila }) => celda(reporte, c.clave, fila[c.clave]),
  }));
  const filas: Fila[] = pagina.items.map((fila, i) => ({ fila, i }));
  return (
    <DataTable
      ariaLabel={reporte.nombre}
      columns={columnas}
      rows={filas}
      getRowKey={({ fila, i }) => String(fila.itemId ?? fila.liquidacionId ?? "fila") + `-${i}`}
      status="ready"
      minWidth={960}
      pagination={{ page: pagina.page, pageSize: pagina.pageSize, totalCount: pagina.total, onPageChange: onPagina }}
    />
  );
}
