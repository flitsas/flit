"use client";

import dynamic from "next/dynamic";
import { DataTable, InlineAlert, usePaginacion } from "@flit/ui";
import { Cargador } from "@flit/ui/Loader";
import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import { Encabezado } from "@/components/Pantalla";
import { BarrasSimples } from "@/components/inicio/BarrasSimples";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { BarraFiltros, periodoEfectivo, useFiltrosUrl } from "@/components/reportes/gestion/filtrosUrl";
import { apiFetch } from "@/lib/api";
import { formatoFecha, formatoFechaHora } from "@/lib/format/date";
import { numeroCorto, textoPeriodo } from "@/lib/reportes";
import { useCarga } from "@/lib/useCarga";
import { BotonExcel } from "@/components/reportes/gestion/excel";
import { hojasPlataforma } from "./excelPlataforma";
import { FUENTES, TIPOS_ERROR, type ReportePlataforma } from "./tipos";

const GraficoArea = dynamic(() => import("@/components/inicio/GraficoArea").then((m) => m.GraficoArea), { loading: () => <div style={{ height: 240 }} /> });

const n = (x: number) => x.toLocaleString("es-CO");
const fuente = (f: string) => FUENTES[f] ?? f;
const MAX_DIAS = 366;

function diasEntre(desde: string, hasta: string): number {
  return (Date.parse(`${hasta}T00:00:00Z`) - Date.parse(`${desde}T00:00:00Z`)) / 86_400_000;
}

function TablaDias({ filas }: { filas: ReportePlataforma["porDia"] }) {
  const pg = usePaginacion(10);
  const orden = [...filas].reverse();
  return (
    <DataTable
      ariaLabel="Consultas por día y fuente"
      tarjeta={false}
      rows={pg.paginar(orden)}
      getRowKey={(p) => `${p.dia}:${p.fuente}`}
      emptyMessage="Sin consultas en el periodo."
      minWidth={640}
      columns={[
        { key: "dia", header: "Día", render: (p) => formatoFecha(p.dia) },
        { key: "fuente", header: "Fuente", render: (p) => fuente(p.fuente) },
        { key: "consultas", header: "Consultas", align: "right", render: (p) => n(p.consultas) },
        { key: "conError", header: "Con error", align: "right", render: (p) => (p.conError > 0 ? <span className="font-semibold text-[color:var(--badge-danger-fg)]">{n(p.conError)}</span> : "0") },
        { key: "obtenidos", header: "Obtenidos", align: "right", render: (p) => n(p.obtenidos) },
        { key: "nuevos", header: "Nuevos", align: "right", render: (p) => n(p.nuevos) },
      ]}
      pagination={orden.length > 10 ? { page: pg.page, pageSize: pg.pageSize, totalCount: orden.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize } : undefined}
    />
  );
}

function TablaCorreos({ filas }: { filas: ReportePlataforma["correos"]["recientes"] }) {
  const pg = usePaginacion(10);
  return (
    <DataTable
      ariaLabel="Correos fallidos o rebotados más recientes"
      tarjeta={false}
      rows={pg.paginar(filas)}
      getRowKey={(c) => c.id}
      emptyMessage="Ningún correo fallido ni rebotado en el periodo."
      minWidth={760}
      columns={[
        { key: "fecha", header: "Fecha", render: (c) => formatoFechaHora(c.creadoEn) },
        { key: "empresa", header: "Empresa", render: (c) => c.empresa },
        { key: "plantilla", header: "Plantilla", render: (c) => <code className="text-xs">{c.plantilla}</code> },
        { key: "estado", header: "Estado", render: (c) => c.estado },
        { key: "motivo", header: "Motivo", render: (c) => <span className="line-clamp-2">{c.motivo ?? "—"}</span> },
      ]}
      pagination={filas.length > 10 ? { page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize } : undefined}
    />
  );
}

/**
 * Pestaña Plataforma (solo SuperAdmin; V1: «Log de migración» y la estadística diaria). Es el único sitio de Comparendos
 * donde se nombran las fuentes y los errores técnicos (R3).
 */
export function PlataformaReporte() {
  const { filtros, escribir, errorUrl } = useFiltrosUrl();
  const periodo = periodoEfectivo(filtros);
  const errorAnio = !errorUrl && diasEntre(periodo.desde, periodo.hasta) >= MAX_DIAS ? "En Plataforma el rango máximo es de un año." : null;
  const error = errorUrl ?? errorAnio;
  const datos = useCarga(error ? null : `plataforma:${JSON.stringify({ empresa: filtros.empresa, ...periodo })}`, (signal) =>
    apiFetch<ReportePlataforma>("/admin/reportes/plataforma", { signal, query: { empresa: filtros.empresa, desde: periodo.desde, hasta: periodo.hasta } }),
  );
  const p = datos.estado === "ready" ? datos.datos : undefined;
  const totalConsultas = p?.fuentes.reduce((s, f) => s + f.consultas, 0) ?? 0;
  const totalError = p?.fuentes.reduce((s, f) => s + f.conError, 0) ?? 0;
  const totalNuevos = p?.fuentes.reduce((s, f) => s + f.nuevos, 0) ?? 0;
  // Una serie por día con todas las fuentes sumadas.
  const porDia = new Map<string, { consultas: number; conError: number }>();
  for (const d of p?.porDia ?? []) {
    const x = porDia.get(d.dia) ?? { consultas: 0, conError: 0 };
    porDia.set(d.dia, { consultas: x.consultas + d.consultas, conError: x.conError + d.conError });
  }
  const puntos = [...porDia.entries()].map(([dia, x]) => ({ etiqueta: formatoFecha(dia), ...x }));

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Plataforma"
        descripcion="Solo administración FLIT: cómo responden las fuentes de consulta, qué errores técnicos hubo, qué correos fallaron y qué empresas acumulan más problemas."
      />
      <BarraFiltros
        filtros={filtros}
        escribir={escribir}
        conNit={false}
        errorUrl={errorUrl}
        accion={
          <BotonExcel
            corte={{
              pestana: "plataforma",
              titulo: "Reportes › Plataforma (administración FLIT)",
              empresa: filtros.empresa,
              nit: null,
              desde: periodo.desde,
              hasta: periodo.hasta,
              corte: `Del ${formatoFecha(periodo.desde)} al ${formatoFecha(periodo.hasta)}`,
              aclaraciones: [
                "Solo administración FLIT: incluye las fuentes de consulta y los errores técnicos.",
                "Los errores técnicos por tipo solo se calculan con todas las empresas.",
              ],
            }}
            hojas={p ? () => hojasPlataforma(p) : null}
            deshabilitado={datos.estado !== "ready" || !!error}
          />
        }
      />
      {errorAnio && (
        <InlineAlert tone="warning" compact>
          {errorAnio} Acorta el periodo y pulsa «Consultar».
        </InlineAlert>
      )}

      {datos.estado === "loading" && <Cargador />}
      {datos.estado === "error" && (
        <InlineAlert tone="error" title="No se pudo cargar Plataforma">
          {datos.error}{" "}
          <button type="button" className="font-semibold underline" onClick={datos.recargar}>
            Reintentar
          </button>
        </InlineAlert>
      )}

      {p && (
        <>
          <div className={METRICAS_GRID_CLS} data-testid="plataforma-kpis">
            <TarjetaMetrica etiqueta="Consultas a las fuentes" cifra={n(totalConsultas)} pie={textoPeriodo(p.desde, p.hasta)} />
            <TarjetaMetrica
              etiqueta="Consultas con error"
              cifra={n(totalError)}
              colorCifra={totalError === 0 ? "var(--di-text-tertiary)" : "var(--badge-danger-fg)"}
              pie={totalConsultas === 0 ? "Sin consultas" : `${((totalError / totalConsultas) * 100).toLocaleString("es-CO", { maximumFractionDigits: 1 })} % de las consultas`}
            />
            <TarjetaMetrica etiqueta="Comparendos nuevos" cifra={n(totalNuevos)} pie="Encontrados por primera vez" />
            <TarjetaMetrica
              etiqueta="Correos fallidos o rebotados"
              cifra={n(p.correos.fallidos + p.correos.rebotados)}
              colorCifra={p.correos.fallidos + p.correos.rebotados === 0 ? "var(--di-text-tertiary)" : "var(--badge-danger-fg)"}
              pie={`${n(p.correos.fallidos)} fallidos · ${n(p.correos.rebotados)} rebotados`}
            />
          </div>

          <div className="grid grid-cols-1 gap-6 2xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
            <PanelInicio titulo="Consultas por día" subtitulo="Todas las fuentes sumadas." icono="reportes" data-testid="plataforma-serie">
              {puntos.length === 0 ? (
                <p className="text-sm text-[color:var(--di-text-tertiary)]">Sin consultas en el periodo.</p>
              ) : (
                <GraficoArea
                  puntos={puntos}
                  series={[
                    { clave: "consultas", etiqueta: "Consultas", color: "var(--kit-azul)" },
                    { clave: "conError", etiqueta: "Con error", color: "var(--kit-naranja)" },
                  ]}
                  formatoEje={numeroCorto}
                  formato={n}
                />
              )}
            </PanelInicio>
            <PanelInicio titulo="Por fuente" subtitulo="Consultas, errores y comparendos obtenidos." icono="fuentes-oficiales" data-testid="plataforma-fuentes">
              <BarrasSimples
                vacio="Sin consultas en el periodo."
                filas={p.fuentes.map((f) => ({
                  clave: f.fuente,
                  etiqueta: fuente(f.fuente),
                  valor: f.consultas,
                  cifra: `${n(f.consultas)} consultas`,
                  detalle: `${n(f.conError)} con error · ${n(f.obtenidos)} obtenidos · ${n(f.nuevos)} nuevos`,
                }))}
              />
            </PanelInicio>
          </div>

          <PanelInicio titulo="Detalle por día y fuente" icono="lista" data-testid="plataforma-dias">
            <TablaDias filas={p.porDia} />
          </PanelInicio>

          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            {p.erroresPorTipo && (
              <PanelInicio titulo="Errores técnicos por tipo" subtitulo="De todas las empresas." icono="alerta" data-testid="plataforma-errores">
                <BarrasSimples
                  color="var(--kit-naranja)"
                  vacio="Ningún error técnico en el periodo."
                  filas={p.erroresPorTipo.map((e) => ({
                    clave: `${e.fuente}:${e.tipo}`,
                    etiqueta: TIPOS_ERROR[e.tipo] ?? e.tipo,
                    detalle: fuente(e.fuente),
                    valor: e.cantidad,
                    cifra: n(e.cantidad),
                  }))}
                />
              </PanelInicio>
            )}
            <PanelInicio titulo="Empresas con más errores" subtitulo="Consultas con error y correos fallidos o rebotados." icono="empresas" data-testid="plataforma-empresas">
              <BarrasSimples
                color="var(--kit-gris)"
                vacio="Ninguna empresa con errores en el periodo."
                filas={p.empresasConMasErrores.map((e) => ({
                  clave: e.empresaId,
                  etiqueta: e.empresa,
                  valor: e.consultasConError + e.correosFallidos,
                  cifra: n(e.consultasConError + e.correosFallidos),
                  detalle: `${n(e.consultasConError)} consultas con error · ${n(e.correosFallidos)} correos`,
                }))}
              />
            </PanelInicio>
            <PanelInicio titulo="Motivos de los correos fallidos" icono="correo" data-testid="plataforma-motivos">
              <BarrasSimples
                color="var(--kit-amarillo)"
                vacio="Ningún correo fallido ni rebotado en el periodo."
                filas={p.correos.motivos.map((m) => ({ clave: m.motivo, etiqueta: m.motivo, titulo: m.motivo, valor: m.cantidad, cifra: n(m.cantidad) }))}
              />
            </PanelInicio>
          </div>

          <PanelInicio titulo="Correos fallidos o rebotados recientes" subtitulo="Los 50 más recientes del periodo." icono="correo" data-testid="plataforma-correos">
            <TablaCorreos filas={p.correos.recientes} />
          </PanelInicio>
        </>
      )}
    </section>
  );
}
