"use client";

import { useMemo } from "react";
import { DataTable, type DataTableColumn } from "@flit/ui/DataTable";
import { Placa } from "@flit/ui/Placa";
import { TAMANOS_REPORTE, TOPE_DESPLAZAMIENTO } from "@/lib/reportes/catalogo";
import { NOMBRE_REPORTE, VACIO, formatoCelda } from "@/lib/reportes/formato";
import type { PaginaReporteDto, TipoReporte, ValorCelda } from "@/lib/reportes/tipos";

type Fila = Record<string, ValorCelda> & { __clave: string };

const NUMERICOS = new Set(["numero", "moneda", "porcentaje"]);

/**
 * Tabla genérica del módulo de reportes (spec-ui §5.4): las columnas y filas salen de la respuesta (contrato §3–§4) y
 * cada celda se formatea por el tipo de su columna. `DataTable` + `Pagination` del kit con 25/50/100 filas (Q3); el
 * desplazamiento se detiene en 10 000 registros (`pagina × tamano ≤ 10 000`) y se pide acotar el filtro. Celdas largas
 * truncadas con `title`; placas con `Placa`. Exporta y filtra el módulo, no la tabla.
 */
export function TablaReporte({
  tipo,
  pagina,
  cargando,
  tamano,
  onPagina,
  onTamano,
}: {
  tipo: TipoReporte;
  pagina: PaginaReporteDto | null;
  cargando: boolean;
  tamano: number;
  onPagina: (pagina: number) => void;
  onTamano: (tamano: number) => void;
}) {
  const columnas = useMemo<DataTableColumn<Fila>[]>(
    () =>
      (pagina?.columnas ?? []).map((c) => ({
        key: c.clave,
        header: c.titulo,
        align: NUMERICOS.has(c.tipo) ? "right" : "left",
        render: (fila: Fila) => {
          const valor = fila[c.clave];
          if (c.clave === "placa" && typeof valor === "string" && valor) return <Placa placa={valor} />;
          const texto = formatoCelda(valor, c, tipo);
          return texto === VACIO ? (
            <span className="text-[color:var(--di-text-tertiary)]">{VACIO}</span>
          ) : (
            <span className="block max-w-[16rem] truncate" title={texto}>
              {texto}
            </span>
          );
        },
      })),
    [pagina?.columnas, tipo],
  );

  const filas = useMemo<Fila[]>(() => (pagina?.filas ?? []).map((f, i) => ({ ...f, __clave: `${pagina?.pagina ?? 1}-${i}` })), [pagina]);
  const total = pagina?.total ?? 0;
  const alcanzable = Math.min(total, Math.floor(TOPE_DESPLAZAMIENTO / tamano) * tamano);

  return (
    <div className="flex flex-col gap-2">
      <DataTable<Fila>
        ariaLabel={NOMBRE_REPORTE[tipo]}
        columns={columnas}
        rows={filas}
        getRowKey={(f) => f.__clave}
        status={cargando ? "loading" : "ready"}
        minWidth={Math.max(720, columnas.length * 140)}
        tarjeta={false}
        pagination={{
          page: pagina?.pagina ?? 1,
          pageSize: tamano,
          totalCount: alcanzable,
          onPageChange: onPagina,
          onPageSizeChange: onTamano,
          pageSizeOptions: TAMANOS_REPORTE,
        }}
      />
      {!cargando && total > alcanzable ? (
        <p className="text-xs text-[color:var(--di-text-secondary)]">Acota el filtro para ver más registros</p>
      ) : null}
    </div>
  );
}
