"use client";

// Gráficas del informe.
//
// La composición por estado y el histograma van a mano con CSS: son barras horizontales con su
// etiqueta al lado, y construidas con DOM real se leen con un lector de pantalla y se afirman en un
// test sin montar un canvas. La serie temporal sí usa Recharts —que ya es dependencia del repo y lo
// que usan los reportes de empresa— porque ahí hacen falta ejes numerados, tooltip y una línea sobre
// las barras, y reimplementar eso a mano es cómo se llega a una gráfica sin valores.

import { useState } from "react";
import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Legend,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { OtReportSeriesPoint, OtReportSummary, OtReportTimeBucket } from "@/lib/api/ot-metrics";
import { ESTADO_ORDER, estadoMeta, formatInt } from "./report-columns";
import { Empty } from "./shared";

// ── Composición por estado ─────────────────────────────────────────────────────

interface EstadoSlice {
  estado: string;
  value: number;
}

function slicesOf(resumen: OtReportSummary): EstadoSlice[] {
  const byEstado: Record<string, number> = {
    en_revision: resumen.enRevision,
    esperando_placa: resumen.esperandoPlaca,
    esperando_cliente: resumen.esperandoCliente,
    en_subsanacion: resumen.enSubsanacion,
    aprobado: resumen.aprobados,
    rechazado: resumen.rechazados,
    anulado: resumen.anulados,
    otro: resumen.otros,
  };

  return ESTADO_ORDER.map((estado) => ({ estado, value: byEstado[estado] ?? 0 })).filter(
    (s) => s.value > 0,
  );
}

/**
 * Barra apilada de en qué acabó todo lo recibido.
 *
 * Es la pieza que hace visible el invariante del informe: los tramos suman exactamente el total. En
 * el panel operativo el desglose no cierra —solo expone lo accionable— y esa diferencia hay que
 * poder verla, no leerla en una nota al pie.
 */
export function EstadoComposition({ resumen }: { resumen: OtReportSummary }) {
  const slices = slicesOf(resumen);

  if (resumen.total === 0) {
    return <Empty>No se recibió ningún trámite en el periodo seleccionado.</Empty>;
  }

  return (
    <div className="flex flex-col gap-3" data-testid="ot-report-composicion">
      <div
        className="flex h-7 w-full overflow-hidden rounded-lg"
        role="img"
        aria-label={`Composición de ${resumen.total} trámites recibidos por estado`}
      >
        {slices.map((slice) => {
          const meta = estadoMeta(slice.estado);
          const pct = (slice.value / resumen.total) * 100;
          return (
            <div
              key={slice.estado}
              className="h-full"
              style={{ width: `${pct}%`, background: meta.color }}
              title={`${meta.label}: ${slice.value} (${pct.toFixed(1)} %)`}
            />
          );
        })}
      </div>

      <ul className="flex flex-wrap gap-x-4 gap-y-1.5">
        {slices.map((slice) => {
          const meta = estadoMeta(slice.estado);
          const pct = (slice.value / resumen.total) * 100;
          return (
            <li key={slice.estado} className="flex items-center gap-1.5 text-[11px]" title={meta.hint}>
              <span
                aria-hidden="true"
                className="h-2.5 w-2.5 shrink-0 rounded-sm"
                style={{ background: meta.color }}
              />
              <span className="font-semibold">{meta.label}</span>
              <span className="tabular-nums text-[#6B7280] dark:text-white/50">
                {formatInt(slice.value)} · {pct.toFixed(1)} %
              </span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

// ── Serie temporal ─────────────────────────────────────────────────────────────

const GRANULARIDAD_LABEL: Record<string, string> = {
  dia: "por día",
  semana: "por semana",
  mes: "por mes",
};

export function granularidadLabel(granularidad: string): string {
  return GRANULARIDAD_LABEL[granularidad] ?? granularidad;
}

/** Un periodo de la serie ya listo para dibujar, con el pendiente acumulado calculado. */
export interface TrendPoint extends OtReportSeriesPoint {
  decididos: number;
  /** Radicados menos decididos DEL periodo: el saldo que ese periodo dejó o quitó. */
  saldo: number;
  /** Suma corrida de los saldos: la deuda de trabajo que el organismo arrastra. */
  acumulado: number;
}

/**
 * Añade a la serie lo que la gráfica necesita decir y el backend no cuenta: el saldo del periodo y
 * su acumulado.
 *
 * El acumulado es la razón de ser de la gráfica. Tres barras contestan «cuánto entró y cuánto salió»
 * periodo a periodo, pero la pregunta de verdad —«¿estoy despachando al ritmo al que me llega?»— no
 * se ve en las barras: se ve en si la línea sube. Es un acumulado DENTRO del rango, no la cola real
 * del organismo; arranca en cero el primer periodo porque lo anterior al rango no se consultó.
 */
export function buildTrendData(serie: OtReportSeriesPoint[]): TrendPoint[] {
  let acumulado = 0;
  return serie.map((point) => {
    const decididos = point.aprobados + point.rechazados;
    const saldo = point.radicados - decididos;
    acumulado += saldo;
    return { ...point, decididos, saldo, acumulado };
  });
}

const SERIES = [
  { key: "radicados", label: "Radicados", color: "#557EFF" },
  { key: "aprobados", label: "Aprobados", color: "#8CC63F" },
  { key: "rechazados", label: "Rechazados", color: "#FF4E00" },
] as const;

const ACUMULADO_COLOR = "#F9AC00";

/**
 * Radicados contra decisiones a lo largo del periodo, con el pendiente acumulado encima.
 *
 * Las barras y la línea comparten eje X pero no eje Y: las barras cuentan trámites por periodo y la
 * línea cuenta un saldo que puede ser negativo. Mezclarlos en un solo eje aplastaría las barras cada
 * vez que el acumulado creciera.
 *
 * La serie llega COMPLETA del backend, con los periodos vacíos en cero: por eso aquí no hay que
 * rellenar huecos ni la gráfica puede dibujar una continuidad que no existió.
 */
export function TrendChart({
  serie,
  granularidad,
  onZoom,
}: {
  serie: OtReportSeriesPoint[];
  granularidad: string;
  /** Acota el informe a un periodo. Sin esto la gráfica sería un dibujo y no un control. */
  onZoom?: (desde: string, hasta: string, label: string) => void;
}) {
  const [verValores, setVerValores] = useState(false);

  if (serie.length === 0) {
    return <Empty>No hay periodos que graficar en el rango seleccionado.</Empty>;
  }

  const data = buildTrendData(serie);
  const hayDatos = data.some((p) => p.radicados + p.decididos > 0);

  const totalRadicados = data.reduce((sum, p) => sum + p.radicados, 0);
  const totalDecididos = data.reduce((sum, p) => sum + p.decididos, 0);
  const saldoFinal = data[data.length - 1]?.acumulado ?? 0;

  // Con muchos periodos las etiquetas del eje se pisan. Se saltean en vez de rotarlas: un eje en
  // diagonal se lee peor que uno con la mitad de marcas.
  const tickInterval = Math.max(0, Math.ceil(data.length / 12) - 1);

  return (
    <div className="flex flex-col gap-3" data-testid="ot-report-tendencia">
      {/* Los titulares van en texto y no solo en la gráfica: son la respuesta de una línea, y
          obligar a pasar el ratón por encima para leer el dato principal es esconderlo. */}
      <div className="flex flex-wrap items-center gap-x-5 gap-y-1 text-xs">
        <span>
          Recibí <strong className="tabular-nums">{formatInt(totalRadicados)}</strong> y decidí{" "}
          <strong className="tabular-nums">{formatInt(totalDecididos)}</strong>
        </span>
        <span
          className="rounded-full px-2.5 py-0.5 text-[11px] font-semibold"
          style={{
            background: saldoFinal > 0 ? "#F9AC001A" : "#8CC63F1A",
            color: saldoFinal > 0 ? "#B47800" : "#5B8A22",
          }}
          data-testid="ot-report-tendencia-saldo"
        >
          {saldoFinal > 0
            ? `Se acumularon ${formatInt(saldoFinal)} sin decidir`
            : saldoFinal < 0
              ? `Se descargaron ${formatInt(Math.abs(saldoFinal))} de la cola`
              : "La cola quedó igual que como empezó"}
        </span>
      </div>

      {!hayDatos ? (
        <Empty>
          No hubo movimiento en el periodo. Los {serie.length} periodos aparecen en cero: es un
          silencio real, no un hueco de la gráfica.
        </Empty>
      ) : (
        <>
          <div className="h-64" data-testid="ot-report-tendencia-chart">
            <ResponsiveContainer width="100%" height="100%">
              <ComposedChart
                data={data}
                margin={{ left: 0, right: 8, top: 8, bottom: 4 }}
                onClick={(state: { activePayload?: { payload?: TrendPoint }[] }) => {
                  const point = state?.activePayload?.[0]?.payload;
                  if (point && onZoom) onZoom(point.desde, point.hasta, point.label);
                }}
              >
                <CartesianGrid strokeDasharray="3 3" stroke="#DFE5ED" vertical={false} />
                <XAxis dataKey="label" tick={{ fontSize: 10 }} interval={tickInterval} />
                <YAxis yAxisId="left" tick={{ fontSize: 11 }} allowDecimals={false} width={36} />
                <YAxis
                  yAxisId="right"
                  orientation="right"
                  tick={{ fontSize: 11, fill: ACUMULADO_COLOR }}
                  allowDecimals={false}
                  width={36}
                />
                <Tooltip content={<TrendTooltip granularidad={granularidad} />} />
                <Legend wrapperStyle={{ fontSize: 11 }} />
                {SERIES.map((s) => (
                  <Bar
                    key={s.key}
                    yAxisId="left"
                    dataKey={s.key}
                    name={s.label}
                    fill={s.color}
                    radius={[3, 3, 0, 0]}
                    isAnimationActive={false}
                  />
                ))}
                <Line
                  yAxisId="right"
                  type="monotone"
                  dataKey="acumulado"
                  name="Pendiente acumulado"
                  stroke={ACUMULADO_COLOR}
                  strokeWidth={2}
                  dot={false}
                  isAnimationActive={false}
                />
              </ComposedChart>
            </ResponsiveContainer>
          </div>

          <p className="text-[11px] text-[#6B7280] dark:text-white/50">
            Agrupado {granularidadLabel(granularidad)}. La línea es el pendiente acumulado dentro del
            rango: si sube, entra más de lo que sale.
            {onZoom ? " Haz clic en un periodo para acotar el informe a esos días." : ""}
          </p>
        </>
      )}

      {/* Una gráfica no es una fuente consultable: no se puede copiar un valor de ella ni la lee un
          lector de pantalla. La tabla es el mismo dato en texto, y de paso es la vía accesible para
          acotar el informe sin depender del clic sobre un SVG. */}
      <details
        open={verValores}
        onToggle={(e) => setVerValores((e.currentTarget as HTMLDetailsElement).open)}
        data-testid="ot-report-tendencia-valores"
      >
        <summary className="cursor-pointer text-[11px] font-semibold text-[#557EFF]">
          Ver los valores periodo a periodo
        </summary>
        <div className="mt-2 overflow-x-auto">
          <table className="w-full min-w-[28rem] text-xs">
            <thead>
              <tr className="border-b border-[#DFE5ED] text-left text-[11px] uppercase tracking-wide text-[#6B7280] dark:border-white/10 dark:text-white/50">
                <th className="py-1.5 pr-3 font-semibold">Periodo</th>
                <th className="py-1.5 pr-3 font-semibold">Radicados</th>
                <th className="py-1.5 pr-3 font-semibold">Aprobados</th>
                <th className="py-1.5 pr-3 font-semibold">Rechazados</th>
                <th className="py-1.5 pr-3 font-semibold">Acumulado</th>
              </tr>
            </thead>
            <tbody>
              {data.map((point) => (
                <tr key={point.bucket} className="border-b border-[#EEF1F5] dark:border-white/5">
                  <td className="py-1.5 pr-3">
                    {onZoom ? (
                      <button
                        type="button"
                        onClick={() => onZoom(point.desde, point.hasta, point.label)}
                        className="font-semibold underline underline-offset-2 transition hover:text-[#557EFF]"
                        aria-label={`Acotar el informe a ${point.label}`}
                      >
                        {point.label}
                      </button>
                    ) : (
                      point.label
                    )}
                  </td>
                  <td className="py-1.5 pr-3 tabular-nums">{formatInt(point.radicados)}</td>
                  <td className="py-1.5 pr-3 tabular-nums">{formatInt(point.aprobados)}</td>
                  <td className="py-1.5 pr-3 tabular-nums">{formatInt(point.rechazados)}</td>
                  <td className="py-1.5 pr-3 tabular-nums">{formatInt(point.acumulado)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  );
}

/**
 * Tooltip propio en vez del de serie: el de Recharts lista `dataKey: valor` y aquí hacen falta el
 * saldo del periodo y la frase que lo interpreta, que es lo que el usuario venía a saber.
 */
function TrendTooltip({
  active,
  payload,
  granularidad,
}: {
  active?: boolean;
  payload?: { payload: TrendPoint }[];
  granularidad?: string;
}) {
  if (!active || !payload?.length) return null;
  const point = payload[0]!.payload;

  return (
    <div className="rounded-xl bg-[#162744]/95 px-3 py-2 text-[11px] text-white shadow-lg">
      <p className="mb-1 font-semibold">{point.label}</p>
      {SERIES.map((s) => (
        <p key={s.key} className="flex items-center gap-1.5">
          <span
            aria-hidden="true"
            className="h-2 w-2 rounded-sm"
            style={{ background: s.color }}
          />
          {s.label}: <span className="tabular-nums font-semibold">{formatInt(point[s.key])}</span>
        </p>
      ))}
      <p className="mt-1 border-t border-white/20 pt-1 text-white/80">
        {point.saldo > 0
          ? `Entraron ${formatInt(point.saldo)} más de los que salieron`
          : point.saldo < 0
            ? `Salieron ${formatInt(Math.abs(point.saldo))} más de los que entraron`
            : "Entró y salió lo mismo"}
      </p>
      <p className="text-white/60">
        Acumulado: <span className="tabular-nums">{formatInt(point.acumulado)}</span>
        {granularidad ? ` · ${granularidadLabel(granularidad)}` : ""}
      </p>
    </div>
  );
}

// ── Histograma de tiempos ──────────────────────────────────────────────────────

/**
 * Distribución de los tiempos de decisión.
 *
 * Acompaña a la mediana porque un p50 solo no distingue «casi todo sale en un día» de «la mitad sale
 * en un día y la otra mitad en tres semanas». El histograma sí.
 */
export function TimeHistogram({ buckets }: { buckets: OtReportTimeBucket[] }) {
  const total = buckets.reduce((sum, b) => sum + b.tramites, 0);

  if (total === 0) {
    return <Empty>Ningún trámite del periodo llegó a una decisión todavía.</Empty>;
  }

  // Del verde al rojo según el tramo: el color hace el trabajo de decir qué tramo es bueno.
  const colors = ["#8CC63F", "#00DBD5", "#557EFF", "#F9AC00", "#FF4E00"];

  return (
    <div className="flex flex-col gap-2" data-testid="ot-report-histograma">
      {buckets.map((bucket, i) => {
        const pct = (bucket.tramites / total) * 100;
        return (
          <div
            key={bucket.key}
            className="grid grid-cols-[minmax(7rem,9rem)_1fr_auto] items-center gap-3 text-xs"
          >
            <span>{bucket.label}</span>
            <span className="h-2 overflow-hidden rounded bg-[#EEF1F5] dark:bg-white/10">
              <span
                className="block h-full rounded"
                style={{
                  width: `${bucket.tramites === 0 ? 0 : Math.max(1, pct)}%`,
                  background: colors[i] ?? "#557EFF",
                }}
              />
            </span>
            <span className="tabular-nums font-semibold">
              {formatInt(bucket.tramites)}{" "}
              <span className="font-normal text-[#6B7280] dark:text-white/50">
                ({pct.toFixed(0)} %)
              </span>
            </span>
          </div>
        );
      })}
    </div>
  );
}
