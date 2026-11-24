"use client";

import dynamic from "next/dynamic";
import { DataTable, InlineAlert, usePaginacion, type DataTableColumn } from "@flit/ui";
import { Cargador } from "@flit/ui/Loader";
import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { useSesion } from "@/components/ComparendosShell";
import { useAbrirDetalle } from "@/components/detalle/useAbrirDetalle";
import { Encabezado } from "@/components/Pantalla";
import { BarrasSimples } from "@/components/inicio/BarrasSimples";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { BarraFiltros, periodoEfectivo, useFiltrosUrl } from "@/components/reportes/gestion/filtrosUrl";
import { apiFetch } from "@/lib/api";
import { formatoFecha, formatoPesos } from "@/lib/format/date";
import { formatoTasa, numeroCorto, pesosCorto, textoPeriodo } from "@/lib/reportes";
import { useCarga } from "@/lib/useCarga";
import type { FilaDp, ReporteDp, RespuestaDp } from "./tipos";

/** recharts se carga aparte: las cifras y las tablas pintan sin esperarlo. */
const GraficoArea = dynamic(() => import("@/components/inicio/GraficoArea").then((m) => m.GraficoArea), { loading: () => <div style={{ height: 240 }} /> });

const n = (x: number) => x.toLocaleString("es-CO");
const dias = (d: number | null) => (d === null ? "—" : String(d).replace(".", ","));

/** Tabla con su paginación (cada una con su propio estado). */
function Tabla<T>({ titulo, filas, columnas, clave, vacio, minWidth, onFila, pie }: {
  titulo: string;
  filas: T[];
  columnas: DataTableColumn<T>[];
  clave: (f: T) => string;
  vacio: string;
  minWidth: number;
  onFila?: (f: T) => void;
  pie?: string;
}) {
  const pg = usePaginacion(10);
  return (
    <div className="flex min-w-0 flex-col gap-2">
      <DataTable
        ariaLabel={titulo}
        tarjeta={false}
        rows={pg.paginar(filas)}
        getRowKey={clave}
        onRowClick={onFila}
        emptyMessage={vacio}
        minWidth={minWidth}
        columns={columnas}
        pagination={filas.length > 10 ? { page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize } : undefined}
      />
      {pie && <p className="text-xs text-[color:var(--di-text-tertiary)]">{pie}</p>}
    </div>
  );
}

const COLUMNAS_FILA = (primera: string): DataTableColumn<FilaDp>[] => [
  { key: "nombre", header: primera, render: (f) => <span className="font-medium">{f.nombre}</span> },
  { key: "enviados", header: "Enviados", align: "right", render: (f) => n(f.enviados) },
  { key: "favorables", header: "Favorables", align: "right", render: (f) => n(f.favorables) },
  { key: "desfavorables", header: "Desfavorables", align: "right", render: (f) => n(f.desfavorables) },
  { key: "sinRespuesta", header: "Sin respuesta", align: "right", render: (f) => n(f.sinRespuesta) },
  { key: "dias", header: "Días hasta la respuesta", align: "right", render: (f) => dias(f.diasPromedio) },
];

/** Más de ~3 meses de días no se leen en el eje: se toma un punto por semana (y siempre el último, que es el total). */
function puntosAcumulado(acumulado: ReporteDp["acumulado"]) {
  const semanal = acumulado.length > 92;
  return acumulado
    .filter((_, i) => !semanal || i % 7 === 0 || i === acumulado.length - 1)
    .map((p) => ({ etiqueta: formatoFecha(p.dia), enviados: p.enviados, respondidos: p.respondidos }));
}

/**
 * Pestaña «Derechos de petición» (Reportes 2.0; V1: «Reporte DP Detallado» y el consolidado de «Resumen de reportes»). La
 * tasa y el ahorro son los del Resumen para el mismo periodo. Enviados: radicados en el periodo; favorables y
 * desfavorables: respuestas recibidas en el periodo.
 */
export function DpReporte() {
  const { sesion } = useSesion();
  const abrir = useAbrirDetalle();
  const { filtros, escribir, errorUrl } = useFiltrosUrl();
  const periodo = periodoEfectivo(filtros);
  const clave = errorUrl ? null : `dp:${JSON.stringify({ ...filtros, ...periodo })}`;
  const datos = useCarga(clave, (signal) =>
    apiFetch<RespuestaDp>("/reportes/dp", { signal, query: { empresa: filtros.empresa, nit: filtros.nit, desde: periodo.desde, hasta: periodo.hasta } }),
  );
  const d = datos.estado === "ready" ? datos.datos?.dp : undefined;
  const texto = textoPeriodo(periodo.desde, periodo.hasta);
  const respondidos = d ? d.favorables + d.desfavorables : 0;

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Derechos de petición"
        descripcion="Resultados de los derechos de petición enviados: por organismo y por infracción, los favorables, los que esperan respuesta y los que ya pasaron su plazo. La tasa de éxito y el ahorro son los mismos del Resumen."
      />
      <BarraFiltros filtros={filtros} escribir={escribir} errorUrl={errorUrl} />

      {datos.estado === "loading" && <Cargador />}
      {datos.estado === "error" && (
        <InlineAlert tone="error" title="No se pudieron cargar los derechos de petición">
          {datos.error}{" "}
          <button type="button" className="font-semibold underline" onClick={datos.recargar}>
            Reintentar
          </button>
        </InlineAlert>
      )}

      {d && (
        <>
          <div className={METRICAS_GRID_CLS} data-testid="dp-kpis">
            <TarjetaMetrica etiqueta="Tasa de éxito" cifra={formatoTasa(d.tasaExito)} pie={`${n(d.favorables)} favorables de ${n(respondidos)} respondidos · ${texto}`} />
            <TarjetaMetrica etiqueta="Ahorro" cifra={pesosCorto(d.ahorro)} title={formatoPesos(d.ahorro)} pie="Valor al radicar de los favorables del periodo" />
            <TarjetaMetrica etiqueta="Enviados" cifra={n(d.enviados)} pie={`Radicados ${texto}`} />
            <TarjetaMetrica
              etiqueta="Esperando respuesta"
              cifra={n(d.sinRespuesta)}
              pie={d.totalVencidos > 0 ? `${n(d.totalVencidos)} ya pasaron su plazo` : "Ninguno con el plazo vencido"}
              colorCifra={d.sinRespuesta === 0 ? "var(--di-text-tertiary)" : undefined}
            />
          </div>

          <PanelInicio titulo="Enviados y respondidos en el periodo" subtitulo="Acumulado día a día." icono="reportes" data-testid="dp-acumulado">
            {d.acumulado.length === 0 ? (
              <p className="text-sm text-[color:var(--di-text-tertiary)]">El periodo todavía no empieza.</p>
            ) : (
              <GraficoArea
                puntos={puntosAcumulado(d.acumulado)}
                series={[
                  { clave: "enviados", etiqueta: "Enviados", color: "var(--kit-azul)" },
                  { clave: "respondidos", etiqueta: "Respondidos", color: "var(--kit-verde)" },
                ]}
                formatoEje={numeroCorto}
                formato={n}
              />
            )}
          </PanelInicio>

          <div className="grid grid-cols-1 gap-6 2xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
            <PanelInicio titulo="Por organismo" subtitulo="Enviados en el periodo y respuestas recibidas en él." icono="empresas" data-testid="dp-por-organismo">
              <Tabla titulo="Derechos de petición por organismo" filas={d.porOrganismo} columnas={COLUMNAS_FILA("Organismo")} clave={(f) => f.clave} vacio="Sin derechos de petición en el periodo." minWidth={720} />
            </PanelInicio>
            <PanelInicio titulo="Por infracción" subtitulo="Las diez con más derechos de petición enviados." icono="documento" data-testid="dp-por-infraccion">
              <BarrasSimples
                color="var(--kit-naranja)"
                vacio="Sin derechos de petición en el periodo."
                filas={d.porInfraccion.slice(0, 10).map((f) => ({
                  clave: f.clave || "sin-codigo",
                  etiqueta: (
                    <>
                      {f.clave && <span className="font-semibold">{f.clave}</span>} <span className="text-[color:var(--di-text-secondary)]">{f.nombre}</span>
                    </>
                  ),
                  titulo: `${f.clave} ${f.nombre}`.trim(),
                  valor: f.enviados,
                  cifra: `${n(f.enviados)} enviados`,
                  detalle: `${n(f.favorables)} favorables · ${n(f.desfavorables)} desfavorables · ${n(f.sinRespuesta)} sin respuesta`,
                }))}
              />
            </PanelInicio>
          </div>

          <PanelInicio titulo="Organismo e infracción" subtitulo="Las combinaciones con más derechos de petición enviados." icono="rejilla" data-testid="dp-cruce">
            <Tabla
              titulo="Derechos de petición por organismo e infracción"
              filas={d.cruce}
              clave={(c) => `${c.organismoId}:${c.infraccion}`}
              vacio="Sin derechos de petición en el periodo."
              minWidth={640}
              columnas={[
                { key: "organismo", header: "Organismo", render: (c) => c.organismo },
                { key: "infraccion", header: "Infracción", render: (c) => <span className="font-semibold">{c.infraccion}</span> },
                { key: "enviados", header: "Enviados", align: "right", render: (c) => n(c.enviados) },
                { key: "favorables", header: "Favorables", align: "right", render: (c) => n(c.favorables) },
                { key: "desfavorables", header: "Desfavorables", align: "right", render: (c) => n(c.desfavorables) },
              ]}
            />
          </PanelInicio>

          <div className="grid grid-cols-1 gap-6 2xl:grid-cols-2">
            <PanelInicio titulo="Favorables" subtitulo={`Respondidos a favor ${texto}. Clic en una fila para abrir el comparendo.`} icono="medalla" data-testid="dp-positivos">
              <Tabla
                titulo="Derechos de petición favorables"
                filas={d.positivos}
                clave={(p) => `${p.comparendoId}:${p.radicado}`}
                onFila={(p) => abrir(p.comparendoId)}
                vacio="Ningún favorable en el periodo."
                minWidth={720}
                pie={d.totalPositivos > d.positivos.length ? `Se muestran los ${n(d.positivos.length)} más recientes de ${n(d.totalPositivos)}.` : undefined}
                columnas={[
                  { key: "numero", header: "Comparendo", render: (p) => <span className="font-semibold">{p.numero}</span> },
                  { key: "placa", header: "Placa", render: (p) => p.placa ?? "—" },
                  { key: "radicado", header: "Radicado", render: (p) => p.radicado },
                  { key: "organismo", header: "Organismo", render: (p) => p.organismo },
                  { key: "respuesta", header: "Respuesta", render: (p) => formatoFecha(p.fechaRespuesta) },
                  { key: "valor", header: "Valor al radicar", align: "right", render: (p) => formatoPesos(p.valorAlRadicar) },
                ]}
              />
            </PanelInicio>
            <PanelInicio titulo="Vencidos sin respuesta" subtitulo="A hoy: el organismo no respondió dentro de su plazo. Clic en una fila para abrir el comparendo." icono="vencido" data-testid="dp-vencidos">
              <Tabla
                titulo="Derechos de petición vencidos sin respuesta"
                filas={d.vencidos}
                clave={(v) => `${v.comparendoId}:${v.radicado}`}
                onFila={(v) => abrir(v.comparendoId)}
                vacio="Ninguno pasó su plazo sin respuesta."
                minWidth={720}
                pie={d.totalVencidos > d.vencidos.length ? `Se muestran los ${n(d.vencidos.length)} más antiguos de ${n(d.totalVencidos)}.` : undefined}
                columnas={[
                  { key: "numero", header: "Comparendo", render: (v) => <span className="font-semibold">{v.numero}</span> },
                  { key: "placa", header: "Placa", render: (v) => v.placa ?? "—" },
                  { key: "radicado", header: "Radicado", render: (v) => v.radicado },
                  { key: "organismo", header: "Organismo", render: (v) => v.organismo },
                  { key: "limite", header: "Plazo", render: (v) => formatoFecha(v.fechaLimite) },
                  { key: "dias", header: "Días vencido", align: "right", render: (v) => n(v.diasVencido) },
                ]}
              />
            </PanelInicio>
          </div>

          {sesion?.usuario.esSuperAdmin && d.consolidado && (
            <PanelInicio titulo="Consolidado por empresa" subtitulo="Todas las empresas: cada empresa ante cada organismo." icono="empresas" data-testid="dp-consolidado">
              <Tabla
                titulo="Consolidado de derechos de petición por empresa y organismo"
                filas={d.consolidado}
                clave={(c) => `${c.empresaId}:${c.organismoId}`}
                vacio="Sin derechos de petición en el periodo."
                minWidth={760}
                columnas={[
                  { key: "empresa", header: "Empresa", render: (c) => <span className="font-medium">{c.empresa}</span> },
                  { key: "organismo", header: "Organismo", render: (c) => c.organismo },
                  { key: "enviados", header: "Enviados", align: "right", render: (c) => n(c.enviados) },
                  { key: "favorables", header: "Favorables", align: "right", render: (c) => n(c.favorables) },
                  { key: "desfavorables", header: "Desfavorables", align: "right", render: (c) => n(c.desfavorables) },
                  { key: "sinRespuesta", header: "Sin respuesta", align: "right", render: (c) => n(c.sinRespuesta) },
                ]}
              />
            </PanelInicio>
          )}
        </>
      )}
    </section>
  );
}
