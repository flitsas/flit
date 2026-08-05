"use client";

// Gráficas del informe.
//
// Hechas a mano con CSS en vez de una librería de charts: el módulo OT no arrastra ninguna, las tres
// formas que hacen falta aquí son barras, y una gráfica construida con elementos reales del DOM se
// puede leer con un lector de pantalla y afirmar en un test sin montar un canvas.

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

/**
 * Radicados contra decisiones a lo largo del periodo.
 *
 * Se pintan las tres series juntas porque la pregunta que responde la gráfica no es «cuántos
 * radicaron» sino «¿estoy despachando al ritmo al que me llega?». Con una sola serie eso no se ve.
 *
 * La serie llega COMPLETA del backend, con los periodos vacíos en cero: por eso aquí no hay que
 * rellenar huecos ni la gráfica puede dibujar una continuidad que no existió.
 */
export function TrendChart({
  serie,
  granularidad,
}: {
  serie: OtReportSeriesPoint[];
  granularidad: string;
}) {
  if (serie.length === 0) {
    return <Empty>No hay periodos que graficar en el rango seleccionado.</Empty>;
  }

  const max = Math.max(1, ...serie.flatMap((p) => [p.radicados, p.aprobados, p.rechazados]));
  const hayDatos = serie.some((p) => p.radicados + p.aprobados + p.rechazados > 0);

  // Con muchos periodos las etiquetas se pisan; se muestran salteadas para que el eje siga siendo
  // legible en vez de convertirse en una mancha.
  const labelStep = Math.ceil(serie.length / 12);

  const series = [
    { key: "radicados" as const, label: "Radicados", color: "#557EFF" },
    { key: "aprobados" as const, label: "Aprobados", color: "#8CC63F" },
    { key: "rechazados" as const, label: "Rechazados", color: "#FF4E00" },
  ];

  return (
    <div className="flex flex-col gap-3" data-testid="ot-report-tendencia">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
        {series.map((s) => (
          <span key={s.key} className="flex items-center gap-1.5 text-[11px]">
            <span
              aria-hidden="true"
              className="h-2.5 w-2.5 rounded-sm"
              style={{ background: s.color }}
            />
            {s.label}
          </span>
        ))}
        <span className="text-[11px] text-[#6B7280] dark:text-white/50">
          Agrupado {granularidadLabel(granularidad)} · máximo {formatInt(max)}
        </span>
      </div>

      {!hayDatos ? (
        <Empty>
          No hubo movimiento en el periodo. Los {serie.length} periodos aparecen en cero: es un
          silencio real, no un hueco de la gráfica.
        </Empty>
      ) : (
        <div className="overflow-x-auto">
          {/* Las columnas se estiran a la altura del contenedor (`h-full` sobre un padre con altura
              definida) y solo las BARRAS se alinean abajo. Envolverlas en un contenedor con
              `items-end` dejaba a la columna con altura natural — o sea cero— y las barras, que se
              miden en porcentaje, desaparecían por completo. */}
          <div className="flex min-w-full gap-1" style={{ height: "10rem" }}>
            {serie.map((point) => (
              <div
                key={point.bucket}
                className="flex h-full min-w-[1.5rem] flex-1 items-end justify-center gap-[2px]"
              >
                {series.map((s) => {
                  const value = point[s.key];
                  return (
                    <div
                      key={s.key}
                      className="w-1/4 rounded-t-sm transition-all"
                      style={{
                        // Un mínimo visible para los valores > 0: una barra de 0,3 px se lee como
                        // ausencia, y confundir «uno» con «ninguno» es justo lo que no puede pasar.
                        height: value === 0 ? "0" : `${Math.max(3, (value / max) * 100)}%`,
                        background: s.color,
                      }}
                      title={`${point.label} · ${s.label}: ${value}`}
                    />
                  );
                })}
              </div>
            ))}
          </div>

          <div className="mt-1 flex min-w-full gap-1">
            {serie.map((point, i) => (
              <div
                key={point.bucket}
                className="min-w-[1.5rem] flex-1 text-center text-[9px] text-[#6B7280] dark:text-white/50"
              >
                {i % labelStep === 0 ? point.label : ""}
              </div>
            ))}
          </div>
        </div>
      )}
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
