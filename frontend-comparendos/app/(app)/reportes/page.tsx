"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { DataTable, InlineAlert, usePaginacion } from "@flit/ui";
import { ChipFiltro, CHIPS_FILTRO_FILA_CLS } from "@flit/ui/ChipsFiltro";
import { Cargador } from "@flit/ui/Loader";
import { useSesion } from "@/components/ComparendosShell";
import { Encabezado } from "@/components/Pantalla";
import { FiltrosPeriodo, periodoInicial, type Periodo } from "@/components/reportes/FiltrosPeriodo";
import { BarrasApiladas } from "@/components/inicio/BarrasApiladas";
import { PanelInicio } from "@/components/inicio/PanelInicio";
import { RequiereReportes } from "@/components/reportes/RequiereReportes";
import { ETIQUETA_CLS, INPUT_CLS, controlCls } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { formatoFecha, formatoPesos } from "@/lib/format/date";
import { P } from "@/lib/nav";
import {
  etiquetaMes,
  formatoTasa,
  nombreMes,
  numeroCorto,
  pesosCorto,
  SERIES,
  valorSerie,
  type Dashboard as DatosDashboard,
  type RespuestaEstados,
  type Serie,
} from "@/lib/reportes";
import { tienePermiso } from "@/lib/sesion";
import type { Nit } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";
import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";

const AZUL = "var(--kit-azul)";
const AZUL_CLARO = "color-mix(in srgb, var(--kit-azul) 35%, transparent)";
const PAGOS = [
  { etiqueta: "CC · cambio de contraventor", color: "var(--kit-azul)", clave: "pagosCc" },
  { etiqueta: "PC · pagó el contraventor", color: "var(--kit-verde)", clave: "pagosPc" },
  { etiqueta: "BC · banco / empresa", color: "var(--kit-naranja)", clave: "pagosBc" },
] as const;

/** Cifra del tablero con la tarjeta de la propuesta (HU #14486, `TarjetaMetrica` de @flit/ui). */
function Kpi({ titulo, valor, detalle, title, ariaLabel, onClick }: { titulo: string; valor: string; detalle?: string; title?: string; ariaLabel?: string; onClick?: () => void }) {
  return <TarjetaMetrica etiqueta={titulo} cifra={valor} pie={detalle} title={title} aria-label={ariaLabel} onClick={onClick} />;
}

function SelectorNit({ empresa, valor, onCambio }: { empresa: string | null; valor: string | null; onCambio: (id: string | null) => void }) {
  const nits = useCarga(`nits-reporte:${empresa}`, (signal) => apiFetch<Nit[]>("/monitoreo/nits", { signal, query: { empresa } }));
  return (
    <label className={ETIQUETA_CLS}>
      NIT
      <select className={`${INPUT_CLS} w-full sm:w-60`} value={valor ?? ""} onChange={(e) => onCambio(e.target.value || null)} disabled={nits.estado !== "ready"}>
        <option value="">Todos los NIT</option>
        {(nits.datos ?? []).map((n) => (
          <option key={n.id} value={n.id}>
            {n.numero} {n.nombreTitular ? `· ${n.nombreTitular}` : ""}
          </option>
        ))}
      </select>
    </label>
  );
}

function Contenido() {
  const { sesion } = useSesion();
  const [periodo, setPeriodo] = useState<Periodo>(periodoInicial);
  const [nit, setNit] = useState<string | null>(null);
  const [serie, setSerie] = useState<Serie>("ingresados");
  const router = useRouter();
  const pg = usePaginacion(10);

  const datos = useCarga(JSON.stringify({ ...periodo, nit }), (signal) =>
    apiFetch<DatosDashboard>("/reportes/dashboard", { signal, query: { empresa: periodo.empresa, nit, desde: periodo.desde, hasta: periodo.hasta } }),
  );
  // Solo el reparto de la deuda (lo que se puede cobrar frente a lo caducado o prescrito) para rotular bien el indicador:
  // ?partes=estados es una consulta, no las ~11 de la lente completa del Inicio.
  const dinero = useCarga(`dinero:${JSON.stringify({ ...periodo, nit })}`, (signal) =>
    apiFetch<RespuestaEstados>("/reportes/dinero", { signal, query: { empresa: periodo.empresa, nit, desde: periodo.desde, hasta: periodo.hasta, partes: "estados" } }),
  );
  const reparto = dinero.datos?.dinero;
  const d = datos.datos;
  const empresaNits = sesion?.usuario.esSuperAdmin ? periodo.empresa : (sesion?.empresa?.id ?? null);
  const puedeVerNits = !!empresaNits && tienePermiso(sesion, P.monitoreoVer);
  const def = SERIES.find((s) => s.clave === serie) ?? SERIES[0];
  const formato = (n: number) => (def.pesos ? formatoPesos(n) : n.toLocaleString("es-CO"));
  const formatoEje = (n: number) => (def.pesos ? (n === 0 ? "0" : `$${numeroCorto(n)}`) : numeroCorto(n));
  const anioActual = Number(periodo.hasta.slice(0, 4));

  const aplicar = (p: Periodo) => {
    if (p.empresa !== periodo.empresa) setNit(null);
    setPeriodo(p);
  };

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Dashboard ejecutivo"
        descripcion="Deuda, resultados de los derechos de petición, ahorro y lo que está por vencer. La deuda y el radar son a hoy; las tasas y el ahorro, del periodo. La deuda sin pagar incluye lo caducado y lo prescrito."
        acciones={
          tienePermiso(sesion, P.productividadVer) ? (
            <Link href="/reportes/productividad" className={controlCls(false)}>
              Ver productividad
            </Link>
          ) : undefined
        }
      />
      <FiltrosPeriodo
        valor={periodo}
        onAplicar={aplicar}
        extra={puedeVerNits ? <SelectorNit key={empresaNits} empresa={empresaNits} valor={nit} onCambio={setNit} /> : undefined}
      />

      {datos.estado === "loading" && <Cargador />}
      {datos.estado === "error" && (
        <InlineAlert tone="error" title="No se pudo cargar el dashboard">
          {datos.error}{" "}
          <button type="button" className="font-semibold underline" onClick={datos.recargar}>
            Reintentar
          </button>
        </InlineAlert>
      )}

      {d && datos.estado === "ready" && (
        <>
          <div className={METRICAS_GRID_CLS}>
            <Kpi
              titulo="Deuda sin pagar"
              valor={pesosCorto(d.kpis.deudaActivaTotal)}
              title={formatoPesos(d.kpis.deudaActivaTotal)}
              detalle={
                reparto
                  ? `${pesosCorto(reparto.exigible.valor)} se puede cobrar · ${pesosCorto(reparto.noExigible.valor)} ya caducó o prescribió`
                  : `${d.kpis.comparendosConDeuda.toLocaleString("es-CO")} comparendos activos sin pagar, incluidos los caducados y prescritos`
              }
            />
            <Kpi
              titulo="Tasa de éxito de DP"
              valor={formatoTasa(d.kpis.tasaExitoDp)}
              detalle={`${d.kpis.dpFavorables} favorables · ${d.kpis.dpDesfavorables} desfavorables · ${d.kpis.dpSinRespuesta} esperando respuesta`}
            />
            <Kpi titulo="Ahorro acumulado" valor={pesosCorto(d.kpis.ahorroAcumulado)} title={formatoPesos(d.kpis.ahorroAcumulado)} detalle="Valor al radicar de los DP favorables del periodo" />
            <Kpi
              titulo="Radar de vencimientos"
              valor={d.kpis.radarCantidad.toLocaleString("es-CO")}
              detalle={`${pesosCorto(d.kpis.radarValor)} · vencen antes del ${formatoFecha(d.kpis.radarHasta)}`}
              title={formatoPesos(d.kpis.radarValor)}
              ariaLabel={`Radar de vencimientos: ${d.kpis.radarCantidad.toLocaleString("es-CO")} comparendos · ${formatoPesos(d.kpis.radarValor)}. Ver en la bandeja`}
              onClick={() => {
                // Clic en el radar → la bandeja filtrada con el mismo criterio (§7.8).
                const q = new URLSearchParams({ rapido: "Radar" });
                if (nit) q.set("nit", nit);
                if (sesion?.usuario.esSuperAdmin && periodo.empresa) q.set("empresa", periodo.empresa);
                router.push(`/bandeja?${q.toString()}`);
              }}
            />
          </div>

          <div className={CHIPS_FILTRO_FILA_CLS} role="group" aria-label="Indicador de las gráficas">
            {SERIES.map((s) => (
              <ChipFiltro key={s.clave} etiqueta={s.titulo} activo={serie === s.clave} onClick={() => setSerie(s.clave)} />
            ))}
          </div>
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            <PanelInicio titulo={`${def.titulo} · últimos 12 meses`} icono="reportes">
              <BarrasApiladas
                orientacion="vertical"
                filas={d.mensual.map((p) => ({ etiqueta: etiquetaMes(p.periodo), valor: valorSerie(p, serie), pagosCc: p.pagosCc, pagosPc: p.pagosPc, pagosBc: p.pagosBc }))}
                series={serie === "pagos" ? PAGOS : [{ clave: "valor", etiqueta: def.titulo, color: AZUL }]}
                formatoEje={formatoEje}
                formato={formato}
              />
            </PanelInicio>
            <PanelInicio titulo={`${def.titulo} · ${anioActual} contra ${anioActual - 1}`} icono="calendario">
              <BarrasApiladas
                orientacion="vertical"
                agrupadas
                filas={Array.from({ length: 12 }, (_, i) => ({
                  etiqueta: nombreMes(i),
                  anterior: valorSerie(d.anual.anterior[i], serie),
                  actual: valorSerie(d.anual.actual[i], serie),
                }))}
                series={[
                  { clave: "anterior", etiqueta: String(anioActual - 1), color: AZUL_CLARO },
                  { clave: "actual", etiqueta: String(anioActual), color: AZUL },
                ]}
                formatoEje={formatoEje}
                formato={formato}
              />
            </PanelInicio>
          </div>

          <div className="flex flex-col gap-2">
            <h2 className="text-sm font-semibold">Totales por NIT</h2>
            <DataTable
              ariaLabel="Totales por NIT"
              rows={pg.paginar(d.porNit)}
              getRowKey={(f) => f.nitId ?? "sin-nit"}
              emptyMessage="No hay comparendos para estos filtros."
              minWidth={820}
              columns={[
                { key: "nit", header: "NIT", render: (f) => (f.nit ? <span className="font-semibold">{f.nit}</span> : "—") },
                { key: "titular", header: "Titular", render: (f) => f.titular ?? "—" },
                { key: "total", header: "Comparendos", align: "right", render: (f) => f.total.toLocaleString("es-CO") },
                { key: "activos", header: "Activos", align: "right", render: (f) => f.activos.toLocaleString("es-CO") },
                { key: "deuda", header: "Deuda sin pagar", align: "right", render: (f) => formatoPesos(f.deudaActiva) },
                { key: "dp", header: "Con DP", align: "right", render: (f) => f.conDp.toLocaleString("es-CO") },
                { key: "pagados", header: "Pagados", align: "right", render: (f) => f.pagados.toLocaleString("es-CO") },
              ]}
              pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: d.porNit.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
            />
          </div>
        </>
      )}

    </section>
  );
}

/** Dashboard ejecutivo (HU #13500 sobre la API de la HU #13498; §6.16, §7.8). */
export default function Reportes() {
  return (
    <RequiereReportes permiso={P.reportesVer}>
      <Contenido />
    </RequiereReportes>
  );
}
