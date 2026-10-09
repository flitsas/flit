"use client";

import { useState } from "react";
import Link from "next/link";
import { DataTable, InlineAlert, Modal, usePaginacion } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { DetalleComparendo } from "@/components/detalle/DetalleComparendo";
import { Encabezado } from "@/components/Pantalla";
import { FiltrosPeriodo, periodoInicial, type Periodo } from "@/components/reportes/FiltrosPeriodo";
import { GraficoBarras } from "@/components/reportes/GraficoBarras";
import { RequiereReportes } from "@/components/reportes/RequiereReportes";
import { ETIQUETA_CLS, INPUT_CLS, controlCls } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { formatoFecha, formatoPesos } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { etiquetaMes, formatoTasa, nombreMes, SERIES, valorSerie, type ComparendoRadar, type Dashboard as DatosDashboard, type Serie } from "@/lib/reportes";
import { tienePermiso } from "@/lib/sesion";
import type { Nit } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const AZUL = "#557EFF";
const AZUL_CLARO = "#B9C8FF";
const PAGOS = [
  { nombre: "CC · cambio de contraventor", color: "#557EFF", clave: "pagosCc" },
  { nombre: "PC · pagó el contraventor", color: "#16A34A", clave: "pagosPc" },
  { nombre: "BC · banco / empresa", color: "#F59E0B", clave: "pagosBc" },
] as const;

function Kpi({ titulo, valor, detalle, onClick }: { titulo: string; valor: string; detalle?: string; onClick?: () => void }) {
  const contenido = (
    <>
      <span className="text-xs font-semibold text-[#5B6B82] dark:text-slate-300">{titulo}</span>
      <span className="text-2xl font-semibold text-[#162744] dark:text-white">{valor}</span>
      {detalle && <span className="text-xs text-[#5B6B82] dark:text-slate-300">{detalle}</span>}
    </>
  );
  const cls = "flex flex-col gap-1 rounded-2xl border border-[#DFE5ED] p-4 text-left dark:border-white/10";
  return onClick ? (
    <button type="button" className={`${cls} transition hover:border-[#557EFF] focus:outline-none focus:ring-2 focus:ring-[#557EFF]/30`} onClick={onClick}>
      {contenido}
    </button>
  ) : (
    <div className={cls}>{contenido}</div>
  );
}

/** Lista detrás del KPI «Radar de vencimientos»: sin DP ni pago y con la gestión venciendo en los próximos 7 días hábiles. */
function Radar({ empresa, nit, onCerrar, onVer }: { empresa: string | null; nit: string | null; onCerrar: () => void; onVer: (id: string) => void }) {
  const radar = useCarga(`radar:${empresa}:${nit}`, (signal) => apiFetch<ComparendoRadar[]>("/reportes/radar", { signal, query: { empresa, nit } }));
  const pg = usePaginacion(10);
  const filas = radar.datos ?? [];
  return (
    <Modal open onClose={onCerrar} title="Radar de vencimientos" description="Comparendos sin DP ni pago cuya ventana de gestión vence en los próximos 7 días hábiles." size="xl">
      <DataTable
        ariaLabel="Comparendos próximos a vencer"
        rows={pg.paginar(filas)}
        getRowKey={(c) => c.id}
        onRowClick={(c) => onVer(c.id)}
        status={radar.estado}
        errorMessage={radar.error ?? undefined}
        onRetry={radar.recargar}
        emptyMessage="No hay comparendos próximos a vencer."
        columns={[
          { key: "numero", header: "Comparendo", render: (c) => <span className="font-semibold">{c.numero}</span> },
          { key: "placa", header: "Placa", render: (c) => c.placa ?? "—" },
          { key: "vence", header: "Vence la gestión", render: (c) => formatoFecha(c.fechaLimiteGestion) },
          { key: "valor", header: "Valor", align: "right", render: (c) => formatoPesos(c.valorTotal) },
          { key: "ot", header: "Secretaría", render: (c) => c.secretariaTexto ?? "—" },
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />
    </Modal>
  );
}

function SelectorNit({ empresa, valor, onCambio }: { empresa: string | null; valor: string | null; onCambio: (id: string | null) => void }) {
  const nits = useCarga(`nits-reporte:${empresa}`, (signal) => apiFetch<Nit[]>("/monitoreo/nits", { signal, query: { empresa } }));
  return (
    <label className={ETIQUETA_CLS}>
      NIT
      <select className={`${INPUT_CLS} w-60`} value={valor ?? ""} onChange={(e) => onCambio(e.target.value || null)} disabled={nits.estado !== "ready"}>
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
  const [verRadar, setVerRadar] = useState(false);
  const [ver, setVer] = useState<string | null>(null);
  const pg = usePaginacion(10);

  const datos = useCarga(JSON.stringify({ ...periodo, nit }), (signal) =>
    apiFetch<DatosDashboard>("/reportes/dashboard", { signal, query: { empresa: periodo.empresa, nit, desde: periodo.desde, hasta: periodo.hasta } }),
  );
  const d = datos.datos;
  const empresaNits = sesion?.usuario.esSuperAdmin ? periodo.empresa : (sesion?.empresa?.id ?? null);
  const puedeVerNits = !!empresaNits && tienePermiso(sesion, P.monitoreoVer);
  const def = SERIES.find((s) => s.clave === serie) ?? SERIES[0];
  const formato = (n: number) => (def.pesos ? formatoPesos(n) : n.toLocaleString("es-CO"));
  const anioActual = Number(periodo.hasta.slice(0, 4));

  const aplicar = (p: Periodo) => {
    if (p.empresa !== periodo.empresa) setNit(null);
    setPeriodo(p);
  };

  return (
    <section className="flex flex-col gap-6">
      <Encabezado
        titulo="Dashboard ejecutivo"
        descripcion="Deuda, resultados de los derechos de petición, ahorro y lo que está por vencer. La deuda y el radar son a hoy; las tasas y el ahorro, del periodo."
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

      {datos.estado === "loading" && <p className="text-sm opacity-60">Calculando indicadores…</p>}
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
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Kpi titulo="Deuda activa total" valor={formatoPesos(d.kpis.deudaActivaTotal)} detalle={`${d.kpis.comparendosConDeuda.toLocaleString("es-CO")} comparendos activos sin pagar`} />
            <Kpi
              titulo="Tasa de éxito de DP"
              valor={formatoTasa(d.kpis.tasaExitoDp)}
              detalle={`${d.kpis.dpFavorables} favorables · ${d.kpis.dpDesfavorables} desfavorables · ${d.kpis.dpSinRespuesta} esperando respuesta`}
            />
            <Kpi titulo="Ahorro acumulado" valor={formatoPesos(d.kpis.ahorroAcumulado)} detalle="Valor al radicar de los DP favorables del periodo" />
            <Kpi
              titulo="Radar de vencimientos"
              valor={d.kpis.radarCantidad.toLocaleString("es-CO")}
              detalle={`${formatoPesos(d.kpis.radarValor)} · vencen antes del ${formatoFecha(d.kpis.radarHasta)}`}
              onClick={() => setVerRadar(true)}
            />
          </div>

          <div className="flex flex-wrap gap-2" role="group" aria-label="Indicador de las gráficas">
            {SERIES.map((s) => (
              <button key={s.clave} type="button" className={controlCls(serie === s.clave)} aria-pressed={serie === s.clave} onClick={() => setSerie(s.clave)}>
                {s.titulo}
              </button>
            ))}
          </div>
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            <GraficoBarras
              titulo={`${def.titulo} · últimos 12 meses`}
              categorias={d.mensual.map((p) => etiquetaMes(p.periodo))}
              apilado={serie === "pagos"}
              formato={formato}
              series={
                serie === "pagos"
                  ? PAGOS.map((t) => ({ nombre: t.nombre, color: t.color, valores: d.mensual.map((p) => p[t.clave]) }))
                  : [{ nombre: def.titulo, color: AZUL, valores: d.mensual.map((p) => valorSerie(p, serie)) }]
              }
            />
            <GraficoBarras
              titulo={`${def.titulo} · ${anioActual} contra ${anioActual - 1}`}
              categorias={Array.from({ length: 12 }, (_, i) => nombreMes(i))}
              formato={formato}
              series={[
                { nombre: String(anioActual - 1), color: AZUL_CLARO, valores: d.anual.anterior.map((p) => valorSerie(p, serie)) },
                { nombre: String(anioActual), color: AZUL, valores: d.anual.actual.map((p) => valorSerie(p, serie)) },
              ]}
            />
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
                { key: "deuda", header: "Deuda activa", align: "right", render: (f) => formatoPesos(f.deudaActiva) },
                { key: "dp", header: "Con DP", align: "right", render: (f) => f.conDp.toLocaleString("es-CO") },
                { key: "pagados", header: "Pagados", align: "right", render: (f) => f.pagados.toLocaleString("es-CO") },
              ]}
              pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: d.porNit.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
            />
          </div>
        </>
      )}

      {verRadar && <Radar empresa={periodo.empresa} nit={nit} onCerrar={() => setVerRadar(false)} onVer={(id) => { setVerRadar(false); setVer(id); }} />}
      <DetalleComparendo id={ver} onCerrar={() => setVer(null)} />
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
