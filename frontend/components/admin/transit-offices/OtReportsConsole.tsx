"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import {
  fetchOtClientCompanies,
  fetchOtDrilldown,
  fetchOtOperationalPanel,
  fetchOtPerformance,
  fetchOtRejectionReasons,
  OT_DRILLDOWN_BUCKETS,
  type OtClientCompanyOption,
  type OtDrilldown,
  type OtDrilldownBucket,
  type OtMetricsParams,
  type OtOperationalPanel,
  type OtPerformance,
  type OtRejectionReasons,
} from "@/lib/api/ot-metrics";

// Reportes del organismo de tránsito.
//
// Hasta ahora el módulo de reportes solo existía para la empresa gestora: el organismo usaba FLIT
// sin ningún instrumento para ver su propia operación, y esta pantalla era un cartel de "en
// construcción". El eje está invertido respecto a los reportes de empresa — aquí un organismo mira
// hacia las empresas que le radican.

export interface OtReportsConsoleProps {
  transitOfficeId: string;
}

const MODALIDADES = [
  { value: "", label: "Todas las modalidades" },
  { value: "matricula_inicial", label: "Matrícula inicial" },
  { value: "traspaso", label: "Traspaso" },
];

/** Rango por defecto: últimos 30 días, que es la ventana con la que se mira una operación. */
function defaultRange(): { from: string; to: string } {
  const today = new Date();
  const start = new Date(today);
  start.setDate(start.getDate() - 29);
  return { from: toIsoDate(start), to: toIsoDate(today) };
}

function toIsoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

/** Estado del drawer de drill-down: qué bloque se abrió y con qué se pobló. */
interface DrilldownState {
  bucket: OtDrilldownBucket;
  label: string;
  loading: boolean;
  error: string | null;
  data: OtDrilldown | null;
}

export function OtReportsConsole({ transitOfficeId }: OtReportsConsoleProps) {
  const initial = useMemo(defaultRange, []);
  const [from, setFrom] = useState(initial.from);
  const [to, setTo] = useState(initial.to);
  const [modalidad, setModalidad] = useState("");
  const [clientTenantId, setClientTenantId] = useState("");
  const [companies, setCompanies] = useState<OtClientCompanyOption[]>([]);

  const [panel, setPanel] = useState<OtOperationalPanel | null>(null);
  const [performance, setPerformance] = useState<OtPerformance | null>(null);
  const [reasons, setReasons] = useState<OtRejectionReasons | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [drilldown, setDrilldown] = useState<DrilldownState | null>(null);

  // El filtro de empresa se puebla una sola vez por organismo: la lista de clientes no cambia
  // con el rango de fechas ni la modalidad, y recargarla en cada `load()` solo sumaría llamadas.
  useEffect(() => {
    fetchOtClientCompanies(transitOfficeId)
      .then(setCompanies)
      .catch(() => setCompanies([]));
  }, [transitOfficeId]);

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    const params: OtMetricsParams = {
      from,
      to,
      modalidad: modalidad || undefined,
      clientTenantId: clientTenantId || undefined,
      transitOfficeId,
    };
    try {
      // En paralelo: son tres lecturas independientes y encadenarlas solo sumaría espera.
      const [p, perf, rea] = await Promise.all([
        fetchOtOperationalPanel(params),
        fetchOtPerformance(params),
        fetchOtRejectionReasons(params),
      ]);
      setPanel(p);
      setPerformance(perf);
      setReasons(rea);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudieron cargar los reportes");
    } finally {
      setBusy(false);
    }
  }, [from, to, modalidad, clientTenantId, transitOfficeId]);

  useEffect(() => {
    void load();
  }, [load]);

  // Ningún número es un callejón sin salida: abre el bloque pedido con los MISMOS filtros del
  // panel (rango, modalidad, empresa), para que la lista nunca contradiga la tarjeta que la originó.
  const openDrilldown = useCallback(
    (bucket: OtDrilldownBucket, label: string) => {
      setDrilldown({ bucket, label, loading: true, error: null, data: null });
      const params: OtMetricsParams = {
        from,
        to,
        modalidad: modalidad || undefined,
        clientTenantId: clientTenantId || undefined,
        transitOfficeId,
      };
      fetchOtDrilldown(params, bucket)
        .then((data) => setDrilldown({ bucket, label, loading: false, error: null, data }))
        .catch((e: unknown) =>
          setDrilldown({
            bucket,
            label,
            loading: false,
            error: e instanceof Error ? e.message : "No se pudo cargar el detalle.",
            data: null,
          }),
        );
    },
    [from, to, modalidad, clientTenantId, transitOfficeId],
  );

  return (
    <div className="flex flex-col gap-6" data-testid="ot-reports-console">
      <Filters
        from={from}
        to={to}
        modalidad={modalidad}
        clientTenantId={clientTenantId}
        companies={companies}
        busy={busy}
        onFrom={setFrom}
        onTo={setTo}
        onModalidad={setModalidad}
        onClientTenantId={setClientTenantId}
        onReload={() => void load()}
      />

      {error && (
        <p className="rounded-xl bg-red-50 px-4 py-3 text-xs text-red-700 dark:bg-red-500/10 dark:text-red-300">
          {error}
        </p>
      )}

      {panel && <OperationalPanel panel={panel} onOpenDrilldown={openDrilldown} />}
      {reasons && <RejectionReasonsPanel reasons={reasons} />}
      {performance && <PerformancePanel performance={performance} />}

      <DrilldownPanel
        state={drilldown}
        transitOfficeId={transitOfficeId}
        onClose={() => setDrilldown(null)}
      />
    </div>
  );
}

// ── Filtros ────────────────────────────────────────────────────────────────────

function Filters(props: {
  from: string;
  to: string;
  modalidad: string;
  clientTenantId: string;
  companies: OtClientCompanyOption[];
  busy: boolean;
  onFrom: (v: string) => void;
  onTo: (v: string) => void;
  onModalidad: (v: string) => void;
  onClientTenantId: (v: string) => void;
  onReload: () => void;
}) {
  return (
    <div className="flex flex-wrap items-end gap-3">
      <label className="flex flex-col gap-1 text-xs font-semibold">
        Desde
        <input
          type="date"
          value={props.from}
          onChange={(e) => props.onFrom(e.target.value)}
          className="rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
        />
      </label>
      <label className="flex flex-col gap-1 text-xs font-semibold">
        Hasta
        <input
          type="date"
          value={props.to}
          onChange={(e) => props.onTo(e.target.value)}
          className="rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
        />
      </label>
      <label className="flex flex-col gap-1 text-xs font-semibold">
        Modalidad
        <select
          value={props.modalidad}
          onChange={(e) => props.onModalidad(e.target.value)}
          className="rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
        >
          {MODALIDADES.map((m) => (
            <option key={m.value} value={m.value}>
              {m.label}
            </option>
          ))}
        </select>
      </label>
      <label className="flex flex-col gap-1 text-xs font-semibold">
        Empresa
        <select
          value={props.clientTenantId}
          onChange={(e) => props.onClientTenantId(e.target.value)}
          disabled={props.companies.length === 0}
          className="rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF] disabled:opacity-60"
          aria-label="Filtrar por empresa"
        >
          <option value="">Todas las empresas</option>
          {props.companies.map((c) => (
            <option key={c.tenantId} value={c.tenantId}>
              {c.name}
            </option>
          ))}
        </select>
      </label>
      <button
        type="button"
        onClick={props.onReload}
        disabled={props.busy}
        className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
        style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
      >
        {props.busy ? "Cargando…" : "Actualizar"}
      </button>
    </div>
  );
}

// ── A.1 Panel operativo ────────────────────────────────────────────────────────

function OperationalPanel({
  panel,
  onOpenDrilldown,
}: {
  panel: OtOperationalPanel;
  onOpenDrilldown: (bucket: OtDrilldownBucket, label: string) => void;
}) {
  const { movimiento, cola, antiguedad } = panel;
  const explicados = cola.porRevisar + cola.esperandoAsignarPlaca;

  return (
    <Section title="¿Cómo vamos hoy?" testId="ot-reports-operational">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Tile
          value={movimiento.entregadosHoy}
          label="Entregados hoy"
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.entregadosHoy, "Entregados hoy")}
        />
        <Tile
          value={movimiento.decididosHoy}
          label="Decididos hoy"
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.decididosHoy, "Decididos hoy")}
        />
        <Tile
          value={movimiento.pendientesTotal}
          label="Pendientes en total"
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.pendientes, "Pendientes en total")}
        />
        <Tile
          value={
            movimiento.tiempoMedianoDecisionHoras === null
              ? "—"
              : `${movimiento.tiempoMedianoDecisionHoras} h`
          }
          label="Tiempo mediano de decisión"
        />
      </div>

      <SubTitle>En qué está esperando cada trámite</SubTitle>
      <div className="flex flex-col gap-2">
        <Bar
          label="Por revisar"
          value={cola.porRevisar}
          total={movimiento.pendientesTotal}
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.porRevisar, "Por revisar")}
        />
        <Bar
          label="Esperando asignar placa"
          value={cola.esperandoAsignarPlaca}
          total={movimiento.pendientesTotal}
          onClick={() =>
            onOpenDrilldown(OT_DRILLDOWN_BUCKETS.esperandoPlaca, "Esperando asignar placa")
          }
        />
      </div>
      {cola.enEsperaDelCliente > 0 && (
        // El desglose no suma el total a propósito: se decidió no mostrar las esperas del cliente
        // pero sí dejarlas dentro del conteo. Decirlo evita que el número parezca un error.
        <p className="text-[11px] text-amber-700 dark:text-amber-400">
          El desglose suma {explicados} y hay {movimiento.pendientesTotal} pendientes: los{" "}
          <button
            type="button"
            className="underline underline-offset-2 hover:text-amber-900 dark:hover:text-amber-200"
            onClick={() =>
              onOpenDrilldown(OT_DRILLDOWN_BUCKETS.enEsperaDelCliente, "En espera del cliente")
            }
          >
            {cola.enEsperaDelCliente} restantes
          </button>{" "}
          esperan algo del cliente (SOAT o trámite pausado) y no se desglosan aquí.
        </p>
      )}

      <SubTitle>Antigüedad de lo pendiente</SubTitle>
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Bucket
          value={antiguedad.hasta1Dia}
          label="0–1 día"
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.hasta1Dia, "Antigüedad · 0–1 día")}
        />
        <Bucket
          value={antiguedad.entre2y3Dias}
          label="2–3 días"
          onClick={() =>
            onOpenDrilldown(OT_DRILLDOWN_BUCKETS.entre2y3Dias, "Antigüedad · 2–3 días")
          }
        />
        <Bucket
          value={antiguedad.entre4y7Dias}
          label="4–7 días"
          onClick={() =>
            onOpenDrilldown(OT_DRILLDOWN_BUCKETS.entre4y7Dias, "Antigüedad · 4–7 días")
          }
        />
        <Bucket
          value={antiguedad.masDe7Dias}
          label="+7 días"
          hot
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.masDe7Dias, "Antigüedad · +7 días")}
        />
      </div>
      {antiguedad.prioritariosEstancados > 0 && (
        <button
          type="button"
          className="rounded-xl bg-amber-50 px-4 py-3 text-left text-xs text-amber-800 hover:bg-amber-100 dark:bg-amber-500/10 dark:text-amber-300 dark:hover:bg-amber-500/20"
          data-testid="ot-reports-prioritarios"
          onClick={() =>
            onOpenDrilldown(
              OT_DRILLDOWN_BUCKETS.prioritariosEstancados,
              "Prioritarios estancados",
            )
          }
        >
          {antiguedad.prioritariosEstancados}{" "}
          {antiguedad.prioritariosEstancados === 1 ? "trámite marcado" : "trámites marcados"} como
          prioritario llevan más de 3 días sin tocar.
        </button>
      )}
    </Section>
  );
}

// ── A.3 Motivos de rechazo ─────────────────────────────────────────────────────

function RejectionReasonsPanel({ reasons }: { reasons: OtRejectionReasons }) {
  const conDatos = reasons.causales.filter((c) => c.rechazos > 0);

  return (
    <Section title="¿Por qué estoy rechazando?" testId="ot-reports-reasons">
      {reasons.totalRechazos === 0 ? (
        <Empty>No hubo rechazos en el periodo seleccionado.</Empty>
      ) : (
        <>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-3">
            <Tile value={reasons.totalRechazos} label="Rechazos en el periodo" />
            <Tile
              value={reasons.promedioCausalesPorRechazo}
              label="Causales por rechazo (promedio)"
            />
            <Tile value={reasons.rechazosSinCausal} label="Rechazos sin causal marcada" />
          </div>

          <div className="flex flex-col gap-2">
            {conDatos.length === 0 ? (
              <Empty>
                Ningún rechazo del periodo tiene causal del catálogo. Los rechazos anteriores a esta
                funcionalidad solo tienen el motivo escrito a mano.
              </Empty>
            ) : (
              conDatos.map((c) => (
                <Bar
                  key={c.reasonId}
                  label={c.description}
                  value={c.rechazos}
                  total={reasons.totalRechazos}
                  suffix={`${c.pct} %`}
                />
              ))
            )}
          </div>

          {/* La aclaración solo tiene sentido si hay barras que leer. Sin causales, repetirla
              debajo del estado vacío es ruido. */}
          {conDatos.length > 0 && (
            <p className="text-[11px] text-[#6B7280] dark:text-white/50">
              Porcentaje de rechazos que incluyen cada causal. Como un rechazo puede tener varias,
              la suma puede pasar del 100 %.
            </p>
          )}
        </>
      )}
    </Section>
  );
}

// ── A.2 Desempeño ──────────────────────────────────────────────────────────────

function PerformancePanel({ performance }: { performance: OtPerformance }) {
  return (
    <>
      <Section title="Mi equipo de revisores" testId="ot-reports-reviewers">
        {performance.revisores.length === 0 ? (
          <Empty>Nadie decidió trámites en el periodo seleccionado.</Empty>
        ) : (
          <Table
            headers={[
              "Revisor",
              "Decididos",
              "% aprobado",
              "% rechazo",
              "Tiempo mediano",
              "Vuelven a rechazarse",
            ]}
            rows={performance.revisores.map((r) => ({
              key: r.userId,
              cells: [
                r.displayName,
                String(r.decididos),
                `${r.aprobacionPct} %`,
                `${r.rechazoPct} %`,
                r.tiempoMedianoHoras === null ? "—" : `${r.tiempoMedianoHoras} h`,
                `${r.vuelvenARechazarsePct} %`,
              ],
            }))}
          />
        )}
      </Section>

      <Section title="Calidad de lo que me llega, por empresa" testId="ot-reports-companies">
        {performance.empresas.length === 0 ? (
          <Empty>Ninguna empresa entregó trámites en el periodo seleccionado.</Empty>
        ) : (
          <Table
            headers={[
              "Empresa",
              "Entregados",
              "Aprobados",
              "Pasan a la primera",
              "Devoluciones promedio",
            ]}
            rows={performance.empresas.map((e, i) => ({
              key: e.tenantId || `${e.name}-${i}`,
              cells: [
                e.name,
                String(e.entregados),
                String(e.aprobados),
                // Sin aprobados no hay base: «100 %» ahí se leería como una empresa impecable
                // cuando lo que pasa es que no hay nada que medir.
                e.aprobados === 0 ? "—" : `${e.pasanPrimeraPct} %`,
                String(e.devolucionesPromedio),
              ],
            }))}
          />
        )}
      </Section>
    </>
  );
}

// ── Piezas compartidas ─────────────────────────────────────────────────────────

function Section(props: { title: string; testId: string; children: React.ReactNode }) {
  return (
    <section
      className="flex flex-col gap-3 rounded-xl border border-[#DFE5ED] p-4 dark:border-white/10"
      data-testid={props.testId}
    >
      <h3 className="text-sm font-semibold">{props.title}</h3>
      {props.children}
    </section>
  );
}

function SubTitle({ children }: { children: React.ReactNode }) {
  return (
    <p className="mt-1 text-[11px] font-semibold uppercase tracking-wide text-[#6B7280] dark:text-white/50">
      {children}
    </p>
  );
}

function Tile({
  value,
  label,
  onClick,
}: {
  value: number | string;
  label: string;
  onClick?: () => void;
}) {
  const content = (
    <>
      <p className="text-xl font-semibold tabular-nums">{value}</p>
      <p className="text-[11px] text-[#6B7280] dark:text-white/50">{label}</p>
    </>
  );
  if (!onClick) {
    return (
      <div className="rounded-xl border border-[#DFE5ED] px-3 py-2 dark:border-white/10">
        {content}
      </div>
    );
  }
  return (
    <button
      type="button"
      onClick={onClick}
      className="rounded-xl border border-[#DFE5ED] px-3 py-2 text-left transition hover:border-[#557EFF] hover:shadow-sm dark:border-white/10 dark:hover:border-[#557EFF]"
    >
      {content}
    </button>
  );
}

function Bucket({
  value,
  label,
  hot,
  onClick,
}: {
  value: number;
  label: string;
  hot?: boolean;
  onClick?: () => void;
}) {
  // El ámbar es una alarma, no una etiqueta del tramo: en cero no hay nada que atender y un
  // bloque resaltado sin contenido enseña a ignorar el resaltado cuando sí importa.
  const alerta = Boolean(hot) && value > 0;
  const baseCls = alerta
    ? "border-amber-300 bg-amber-50 dark:border-amber-500/40 dark:bg-amber-500/10"
    : "border-[#DFE5ED] dark:border-white/10";
  const content = (
    <>
      <p className={`text-lg font-semibold tabular-nums ${alerta ? "text-amber-800 dark:text-amber-300" : ""}`}>
        {value}
      </p>
      <p className={`text-[11px] ${alerta ? "text-amber-700 dark:text-amber-400" : "text-[#6B7280] dark:text-white/50"}`}>
        {label}
      </p>
    </>
  );
  if (!onClick) {
    return <div className={`rounded-xl border px-3 py-2 text-center ${baseCls}`}>{content}</div>;
  }
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-xl border px-3 py-2 text-center transition hover:shadow-sm ${baseCls}`}
    >
      {content}
    </button>
  );
}

function Bar({
  label,
  value,
  total,
  suffix,
  onClick,
}: {
  label: string;
  value: number;
  total: number;
  suffix?: string;
  onClick?: () => void;
}) {
  const pct = total === 0 ? 0 : Math.min(100, Math.round((value / total) * 100));
  const content = (
    <>
      <span className="truncate" title={label}>
        {label}
      </span>
      <span className="h-2 overflow-hidden rounded bg-[#EEF1F5] dark:bg-white/10">
        <span
          className="block h-full rounded"
          style={{ width: `${pct}%`, background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        />
      </span>
      <span className="tabular-nums font-semibold">{suffix ?? value}</span>
    </>
  );
  if (!onClick) {
    return (
      <div className="grid grid-cols-[minmax(8rem,14rem)_1fr_auto] items-center gap-3 text-xs">
        {content}
      </div>
    );
  }
  return (
    <button
      type="button"
      onClick={onClick}
      className="grid grid-cols-[minmax(8rem,14rem)_1fr_auto] items-center gap-3 rounded-lg text-left text-xs transition hover:bg-[#F5F7FA] dark:hover:bg-white/5"
    >
      {content}
    </button>
  );
}

function Table({
  headers,
  rows,
}: {
  headers: string[];
  rows: { key: string; cells: string[] }[];
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[34rem] text-xs">
        <thead>
          <tr className="border-b border-[#DFE5ED] text-left text-[11px] uppercase tracking-wide text-[#6B7280] dark:border-white/10 dark:text-white/50">
            {headers.map((h) => (
              <th key={h} className="py-2 pr-3 font-semibold">
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} className="border-b border-[#EEF1F5] dark:border-white/5">
              {row.cells.map((cell, i) => (
                <td key={headers[i]} className={`py-2 pr-3 ${i === 0 ? "" : "tabular-nums"}`}>
                  {cell}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Empty({ children }: { children: React.ReactNode }) {
  return <p className="text-xs text-[#6B7280] dark:text-white/50">{children}</p>;
}

// ── Drill-down: qué trámites componen un bloque del panel ──────────────────────

/** Ruta de la bandeja OT filtrada por la placa o el VIN del trámite: "ir a decidirlo" real. */
function procedureDeepLink(
  transitOfficeId: string,
  item: OtDrilldown["items"][number],
): string {
  const params = new URLSearchParams();
  if (item.placa) params.set("placa", item.placa);
  else if (item.vin) params.set("vin", item.vin);
  params.set("status", item.status);
  return `/admin/transit-offices/${transitOfficeId}/client-procedures?${params.toString()}`;
}

function DrilldownPanel({
  state,
  transitOfficeId,
  onClose,
}: {
  state: { bucket: string; label: string; loading: boolean; error: string | null; data: OtDrilldown | null } | null;
  transitOfficeId: string;
  onClose: () => void;
}) {
  if (!state) return null;
  const { label, loading, error, data } = state;

  return (
    <div
      className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-label={label}
      data-testid="ot-reports-drilldown"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="flex max-h-[85dvh] w-full max-w-2xl flex-col rounded-2xl border border-[#DFE5ED] bg-white p-4 shadow-2xl sm:p-6 dark:border-white/10 dark:bg-[#0B0F14]">
        <div className="mb-3 flex shrink-0 items-start justify-between gap-3">
          <h3 className="text-sm font-semibold">{label}</h3>
          <button
            type="button"
            aria-label="Cerrar"
            onClick={onClose}
            className="text-slate-400 hover:text-slate-700 dark:hover:text-white"
          >
            ✕
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto">
          {loading && <p className="text-xs text-[#6B7280] dark:text-white/50">Cargando…</p>}
          {error && (
            <p className="rounded-xl bg-red-50 px-4 py-3 text-xs text-red-700 dark:bg-red-500/10 dark:text-red-300">
              {error}
            </p>
          )}
          {data && data.items.length === 0 && (
            <Empty>Ningún trámite compone este bloque en el periodo seleccionado.</Empty>
          )}
          {data && data.items.length > 0 && (
            <>
              <ul className="flex flex-col gap-2" data-testid="ot-reports-drilldown-list">
                {data.items.map((item) => (
                  <li
                    key={item.procedureInstanceId}
                    className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-[#DFE5ED] px-3 py-2 text-xs dark:border-white/10"
                  >
                    <div className="min-w-0">
                      <p className="font-semibold">
                        {item.referenceNumber}
                        {item.prioritario && (
                          <span className="ml-2 rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold text-amber-800 dark:bg-amber-500/20 dark:text-amber-300">
                            Prioritario
                          </span>
                        )}
                      </p>
                      <p className="truncate text-[11px] text-[#6B7280] dark:text-white/50">
                        {item.clientTenantName}
                        {(item.placa || item.vin) && ` · ${item.placa ?? item.vin}`}
                        {item.diasEsperando !== null && ` · ${item.diasEsperando} días esperando`}
                      </p>
                    </div>
                    <Link
                      href={procedureDeepLink(transitOfficeId, item)}
                      className="shrink-0 rounded-lg px-3 py-1.5 text-[11px] font-semibold text-white"
                      style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
                    >
                      Ir a gestionar
                    </Link>
                  </li>
                ))}
              </ul>
              {data.omitidos > 0 && (
                <p className="mt-3 text-[11px] text-[#6B7280] dark:text-white/50">
                  Se muestran {data.items.length} de {data.total}: {data.omitidos} quedaron fuera
                  por el tope de filas.
                </p>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
