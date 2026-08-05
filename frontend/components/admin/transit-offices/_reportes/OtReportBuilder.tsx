"use client";

// Informe del periodo del organismo de tránsito.
//
// A diferencia del panel operativo —que responde «cómo vamos ahora» y se lee de un vistazo—, esto
// responde una pregunta cerrada sobre un rango: qué recibí, en qué acabó y cuánto tardé. Por eso es
// el único reporte del organismo con detalle fila a fila, columnas a elección y exportación.
//
// El universo son los trámites RECIBIDOS en el rango, no los decididos. Esa elección es la que hace
// que el desglose por estado cierre contra el total, y es lo que separa un informe defendible de una
// suma de indicadores que no cuadran entre sí.

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  fetchOtReport,
  OT_REPORT_SORT,
  type OtClientCompanyOption,
  type OtReport,
  type OtReportRow,
  type OtReportSort,
} from "@/lib/api/ot-metrics";
import { ColumnPicker } from "./ColumnPicker";
import {
  DateRangeFields,
  EmpresaSelect,
  ModalidadSelect,
  RangePresets,
  defaultRange,
  type DateRange,
} from "./filters";
import {
  activePresetId,
  buildReportCsv,
  buildReportXlsx,
  defaultVisibleColumns,
  estadoMeta,
  formatHours,
  formatInt,
  plural,
  REPORT_COLUMNS,
  REPORT_PRESETS,
  reportFileName,
} from "./report-columns";
import { XLSX_MIME } from "@/lib/xlsx";
import { EstadoComposition, TimeHistogram, TrendChart, granularidadLabel } from "./report-visuals";
import { CSV_EXPORT_VISIBLE, Empty, ErrorNotice, PrimaryButton, Section, Tile } from "./shared";

const PAGE_SIZE = 25;

/** Tope de la exportación. Existe para no encadenar decenas de páginas; si se alcanza, se DICE. */
const EXPORT_MAX_ROWS = 2000;
const EXPORT_PAGE_SIZE = 200;

const COLUMNS_STORAGE_KEY = "flit-ot-informe-columnas";

export interface OtReportBuilderProps {
  transitOfficeId: string;
  companies: OtClientCompanyOption[];
}

/** Zoom activo sobre un periodo de la gráfica, con el rango al que hay que poder volver. */
interface ZoomState {
  label: string;
  volverA: DateRange;
}

export function OtReportBuilder({ transitOfficeId, companies }: OtReportBuilderProps) {
  const [range, setRange] = useState<DateRange>(() => defaultRange());
  const [zoom, setZoom] = useState<ZoomState | null>(null);
  const [modalidad, setModalidad] = useState("");
  const [clientTenantId, setClientTenantId] = useState("");

  const [sortBy, setSortBy] = useState<OtReportSort>(OT_REPORT_SORT.radicado);
  const [desc, setDesc] = useState(true);
  const [page, setPage] = useState(1);

  const [visibleColumns, setVisibleColumns] = useState<string[]>(defaultVisibleColumns);
  const [report, setReport] = useState<OtReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exportState, setExportState] = useState<{ busy: boolean; notice: string | null }>({
    busy: false,
    notice: null,
  });

  // La selección de columnas se recupera del navegador DESPUÉS del montaje, no en el estado
  // inicial: leerla durante el render haría que el servidor y el cliente pintaran tablas distintas.
  useEffect(() => {
    try {
      const raw = window.localStorage.getItem(COLUMNS_STORAGE_KEY);
      if (!raw) return;
      const parsed: unknown = JSON.parse(raw);
      if (!Array.isArray(parsed)) return;
      const known = parsed.filter(
        (id): id is string => typeof id === "string" && REPORT_COLUMNS.some((c) => c.id === id),
      );
      // eslint-disable-next-line react-hooks/set-state-in-effect -- rehidratación de preferencia: no hay otro momento para leer localStorage
      if (known.length > 0) setVisibleColumns(known);
    } catch {
      /* modo privado o JSON corrupto: se sigue con las columnas por defecto */
    }
  }, []);

  const applyColumns = useCallback((ids: string[]) => {
    setVisibleColumns(ids);
    try {
      window.localStorage.setItem(COLUMNS_STORAGE_KEY, JSON.stringify(ids));
    } catch {
      /* la preferencia no se persiste, pero la sesión actual sí funciona */
    }
  }, []);

  const params = useMemo(
    () => ({
      from: range.from,
      to: range.to,
      modalidad: modalidad || undefined,
      clientTenantId: clientTenantId || undefined,
      transitOfficeId,
    }),
    [range.from, range.to, modalidad, clientTenantId, transitOfficeId],
  );

  // Cambiar un filtro o el orden devuelve a la primera página: quedarse en la página 7 de un
  // informe que ahora tiene dos es una pantalla vacía que parece un fallo.
  const filterKey = `${params.from}|${params.to}|${params.modalidad ?? ""}|${params.clientTenantId ?? ""}|${sortBy}|${desc}`;
  const lastFilterKey = useRef(filterKey);
  if (lastFilterKey.current !== filterKey && page !== 1) {
    lastFilterKey.current = filterKey;
    setPage(1);
  } else {
    lastFilterKey.current = filterKey;
  }

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setBusy(true);
      setError(null);
      try {
        const data = await fetchOtReport(
          { ...params, page, pageSize: PAGE_SIZE, sortBy, desc },
          signal,
        );
        if (signal?.aborted) return;
        setReport(data);
      } catch (e: unknown) {
        if (signal?.aborted) return;
        setError(e instanceof Error ? e.message : "No se pudo generar el informe.");
      } finally {
        if (!signal?.aborted) setBusy(false);
      }
    },
    [params, page, sortBy, desc],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: patrón del repo, skeleton inmediato antes del fetch
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const toggleSort = useCallback(
    (column: OtReportSort) => {
      if (column === sortBy) {
        setDesc((v) => !v);
        return;
      }
      setSortBy(column);
      setDesc(true);
    },
    [sortBy],
  );

  const handleExport = useCallback(
    async (formato: "xlsx" | "csv") => {
    setExportState({ busy: true, notice: null });
    try {
      const rows: OtReportRow[] = [];
      let current = 1;
      let total = 0;

      // Se exporta el informe COMPLETO, no la página visible: un CSV que solo trae 25 filas cuando
      // en pantalla dice «312 trámites» es una trampa silenciosa.
      for (;;) {
        const chunk = await fetchOtReport({
          ...params,
          page: current,
          pageSize: EXPORT_PAGE_SIZE,
          sortBy,
          desc,
        });
        total = chunk.total;
        rows.push(...chunk.filas);
        if (rows.length >= total || chunk.filas.length === 0 || rows.length >= EXPORT_MAX_ROWS) {
          break;
        }
        current += 1;
      }

      const recortado = rows.length < total;
      const fileName = reportFileName(range.from, range.to, formato);
      if (formato === "xlsx") {
        download(buildReportXlsx(rows, visibleColumns), fileName, XLSX_MIME);
      } else {
        download(buildReportCsv(rows, visibleColumns), fileName, "text/csv;charset=utf-8;");
      }

      setExportState({
        busy: false,
        notice: recortado
          ? `Se exportaron ${formatInt(rows.length)} de ${formatInt(total)} filas: el archivo llegó al tope de ${formatInt(EXPORT_MAX_ROWS)}. Acota el rango o filtra por empresa para llevarte el resto.`
          : `Se exportaron ${plural(rows.length, "fila", "filas")} con ${plural(visibleColumns.length, "columna visible", "columnas visibles")}.`,
      });
    } catch (e: unknown) {
      setExportState({
        busy: false,
        notice: e instanceof Error ? `No se pudo exportar: ${e.message}` : "No se pudo exportar.",
      });
    }
    },
    [params, sortBy, desc, visibleColumns, range.from, range.to],
  );

  // Acotar el informe a un periodo de la gráfica. Se guarda el rango del que se vino para poder
  // deshacerlo: sin eso, un clic en una barra deja al usuario sin forma de volver salvo recordar de
  // memoria las dos fechas que tenía puestas.
  const handleZoom = useCallback(
    (desde: string, hasta: string, label: string) => {
      // Estando ya dentro de un zoom se conserva el rango ORIGINAL: si se guardara el rango
      // actual, dos clics seguidos dejarían el «volver» apuntando al zoom anterior y salir
      // costaría tantos clics como se hubieran dado.
      setZoom({ label, volverA: zoom?.volverA ?? range });
      setRange({ from: desde, to: hasta });
    },
    [range, zoom],
  );

  const salirDelZoom = useCallback(() => {
    if (!zoom) return;
    setRange(zoom.volverA);
    setZoom(null);
  }, [zoom]);

  // Tocar las fechas a mano cancela el zoom: dejar el aviso «acotado a 12 ago» sobre un rango que
  // el usuario acaba de reescribir sería una etiqueta que miente.
  const changeRange = useCallback((next: DateRange) => {
    setZoom(null);
    setRange(next);
  }, []);

  const resumen = report?.resumen;
  const columns = REPORT_COLUMNS.filter((c) => visibleColumns.includes(c.id));
  const totalPages = report ? Math.max(1, Math.ceil(report.total / report.pageSize)) : 1;
  const presetActivo = activePresetId(visibleColumns);
  const exportDisabled = exportState.busy || !report || report.total === 0;

  return (
    <div className="flex flex-col gap-6" data-testid="ot-report-builder">
      <Section
        title="Parámetros del informe"
        testId="ot-report-filters"
        hint="El informe cubre los trámites que el organismo RECIBIÓ dentro del rango. Los estados previos a la radicación no aparecen: el organismo nunca los vio."
      >
        <RangePresets range={range} onChange={changeRange} />
        <div className="flex flex-wrap items-end gap-3">
          <DateRangeFields range={range} onChange={changeRange} />
          <ModalidadSelect value={modalidad} onChange={setModalidad} />
          <EmpresaSelect value={clientTenantId} companies={companies} onChange={setClientTenantId} />
          <PrimaryButton onClick={() => void load()} disabled={busy}>
            {busy ? "Generando…" : "Actualizar"}
          </PrimaryButton>
        </div>

        {zoom && (
          <p
            className="flex flex-wrap items-center gap-2 rounded-xl bg-[#557EFF]/10 px-3 py-2 text-[11px] text-[#2B4AA8] dark:text-[#9DB4FF]"
            data-testid="ot-report-zoom"
          >
            Informe acotado a <strong>{zoom.label}</strong> desde la gráfica.
            <button
              type="button"
              onClick={salirDelZoom}
              className="font-semibold underline underline-offset-2"
            >
              Volver al rango anterior
            </button>
          </p>
        )}
      </Section>

      {error && <ErrorNotice message={error} />}

      {resumen && (
        <>
          <Section
            title="Qué recibí y en qué acabó"
            testId="ot-report-resumen"
            hint="Cada trámite recibido está hoy en exactamente uno de estos estados, así que el desglose suma el total."
          >
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
              <Tile
                value={formatInt(resumen.total)}
                label="Trámites recibidos"
                hint="Entraron a revisión dentro del rango"
              />
              <Tile
                value={formatInt(resumen.aprobados)}
                label="Aprobados"
                accent="#8CC63F"
                hint={pctOf(resumen.aprobados, resumen.total)}
              />
              <Tile
                value={formatInt(resumen.rechazados + resumen.enSubsanacion)}
                label="Rechazados"
                accent="#FF4E00"
                hint={`${formatInt(resumen.enSubsanacion)} con subsanación abierta`}
              />
              <Tile
                value={formatInt(resumen.enRevision + resumen.esperandoPlaca)}
                label="Pendientes en el organismo"
                accent="#557EFF"
                hint="En revisión o esperando placa"
              />
              <Tile
                value={formatInt(resumen.esperandoCliente)}
                label="Esperando al cliente"
                hint="SOAT, impuestos o pausados"
              />
            </div>

            <EstadoComposition resumen={resumen} />
          </Section>

          <Section
            title="Cuánto tardo en decidir"
            testId="ot-report-tiempos"
            hint="Se mide desde la última radicación hasta la decisión: es el turno que trabajó el organismo, sin el tiempo que la empresa tardó en subsanar."
          >
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
              <Tile
                value={formatHours(resumen.tiempoMedianoHoras)}
                label="Mediana (p50)"
                hint="La mitad se decide en este tiempo o menos"
              />
              <Tile
                value={formatHours(resumen.tiempoP90Horas)}
                label="p90"
                hint="9 de cada 10 caben aquí: el compromiso sostenible"
              />
              <Tile
                value={formatHours(resumen.tiempoPromedioHoras)}
                label="Promedio"
                hint="Se desplaza con los casos extremos"
              />
              <Tile
                value={formatHours(resumen.tiempoMedianoAprobacionHoras)}
                label="Mediana hasta aprobar"
                hint="Solo los que terminaron en aprobación"
              />
            </div>

            {resumen.decididos === 0 ? (
              <Empty>
                Ninguno de los {formatInt(resumen.total)} trámites recibidos llegó a una decisión
                todavía, así que no hay tiempos que medir.
              </Empty>
            ) : (
              <>
                <TimeHistogram buckets={resumen.distribucionTiempos} />
                <p className="text-[11px] text-[#6B7280] dark:text-white/50">
                  Calculado sobre {formatInt(resumen.decididos)} de{" "}
                  {plural(resumen.total, "trámite recibido", "trámites recibidos")} con decisión.
                </p>
              </>
            )}
          </Section>

          <Section
            title="Cómo se repartió en el tiempo"
            testId="ot-report-serie"
            hint={`Radicados contra decisiones, agrupado ${granularidadLabel(resumen.granularidad)}. Responde si estoy despachando al ritmo al que me llega.`}
          >
            <TrendChart
              serie={resumen.serie}
              granularidad={resumen.granularidad}
              onZoom={handleZoom}
            />
          </Section>

          <Section
            title="Calidad de lo recibido"
            testId="ot-report-calidad"
            hint="Una devolución es un ciclo extra de trabajo para las dos partes."
          >
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-3">
              <Tile value={formatInt(resumen.devoluciones)} label="Devoluciones en total" />
              <Tile
                value={resumen.devolucionesPromedio.toFixed(2).replace(".", ",")}
                label="Devoluciones por trámite"
                hint="Promedio sobre lo recibido"
              />
              <Tile
                value={formatInt(resumen.decididos)}
                label="Ya decididos"
                hint={pctOf(resumen.decididos, resumen.total)}
              />
            </div>
          </Section>
        </>
      )}

      <Section
        title="Detalle por trámite"
        testId="ot-report-tabla"
        hint="Elige las columnas que quieres ver. Lo que exportes será exactamente esto."
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <ColumnPicker visible={visibleColumns} onChange={applyColumns} columns={REPORT_COLUMNS} />
            {/* El informe acaba en Excel: fechas como fechas y números que se pueden sumar. */}
            <button
              type="button"
              onClick={() => void handleExport("xlsx")}
              disabled={exportDisabled}
              aria-busy={exportState.busy}
              className="rounded-xl px-3 py-2 text-xs font-semibold text-white transition disabled:opacity-60"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
              data-testid="ot-report-export-xlsx"
            >
              {exportState.busy ? "Exportando…" : "Exportar a Excel"}
            </button>
            {CSV_EXPORT_VISIBLE && (
              <button
                type="button"
                onClick={() => void handleExport("csv")}
                disabled={exportDisabled}
                aria-busy={exportState.busy}
                className="rounded-xl border border-[#DFE5ED] px-3 py-2 text-xs font-semibold transition hover:border-[#557EFF] disabled:opacity-60 dark:border-white/10"
                data-testid="ot-report-export"
              >
                CSV
              </button>
            )}
          </div>
        }
      >
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="text-[11px] font-semibold text-[#6B7280] dark:text-white/50">
            Vistas rápidas:
          </span>
          {REPORT_PRESETS.map((preset) => {
            const activa = preset.id === presetActivo;
            return (
              <button
                key={preset.id}
                type="button"
                title={preset.hint}
                // `aria-pressed` y no solo color: el estado de un botón que conmuta tiene que
                // llegar también a quien no ve el borde azul.
                aria-pressed={activa}
                onClick={() => applyColumns(preset.columns)}
                className={`rounded-full border px-3 py-1 text-[11px] font-semibold transition ${
                  activa
                    ? "border-[#557EFF] bg-[#557EFF]/10 text-[#557EFF]"
                    : "border-[#DFE5ED] text-[#6B7280] hover:border-[#557EFF] hover:text-[#557EFF] dark:border-white/10 dark:text-white/50"
                }`}
              >
                {preset.label}
              </button>
            );
          })}
          {/* Quitar o añadir una columna deja de coincidir con cualquier vista. Decirlo evita que
              el usuario crea que sigue en «Gestión» y exporte otra cosa de la que espera. */}
          {presetActivo === null && (
            <span
              className="rounded-full bg-[#F5F7FA] px-3 py-1 text-[11px] font-semibold text-[#6B7280] dark:bg-white/5 dark:text-white/50"
              data-testid="ot-report-preset-personalizada"
            >
              Selección propia
            </span>
          )}
        </div>

        {exportState.notice && (
          <p
            role="status"
            className="rounded-xl bg-[#F5F7FA] px-3 py-2 text-[11px] text-[#6B7280] dark:bg-white/5 dark:text-white/60"
          >
            {exportState.notice}
          </p>
        )}

        {!report || report.filas.length === 0 ? (
          <Empty>
            {busy
              ? "Generando el informe…"
              : "Ningún trámite fue recibido por el organismo en el periodo y con los filtros seleccionados."}
          </Empty>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[40rem] text-xs" data-testid="ot-report-table">
                <thead>
                  <tr className="border-b border-[#DFE5ED] text-left text-[11px] uppercase tracking-wide text-[#6B7280] dark:border-white/10 dark:text-white/50">
                    {columns.map((column) => (
                      <th key={column.id} className="py-2 pr-3 font-semibold">
                        {column.sort ? (
                          <button
                            type="button"
                            onClick={() => toggleSort(column.sort!)}
                            className="flex items-center gap-1 uppercase transition hover:text-[#557EFF]"
                            aria-label={`Ordenar por ${column.label}`}
                          >
                            {column.label}
                            {sortBy === column.sort && (
                              <span aria-hidden="true">{desc ? "↓" : "↑"}</span>
                            )}
                          </button>
                        ) : (
                          column.label
                        )}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {report.filas.map((row) => (
                    <tr
                      key={row.procedureInstanceId}
                      className="border-b border-[#EEF1F5] dark:border-white/5"
                    >
                      {columns.map((column) => (
                        <td
                          key={column.id}
                          className={`py-2 pr-3 ${column.numeric ? "tabular-nums" : ""}`}
                        >
                          {column.id === "estado" ? (
                            <EstadoChip estado={row.estadoOt} />
                          ) : (
                            column.value(row)
                          )}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex flex-wrap items-center justify-between gap-2 text-[11px]">
              <span className="text-[#6B7280] dark:text-white/50">
                {plural(report.total, "trámite", "trámites")} · página {report.page} de {totalPages}
              </span>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  disabled={busy || report.page <= 1}
                  className="rounded-lg border border-[#DFE5ED] px-3 py-1 font-semibold disabled:opacity-40 dark:border-white/10"
                >
                  Anterior
                </button>
                <button
                  type="button"
                  onClick={() => setPage((p) => p + 1)}
                  disabled={busy || report.page >= totalPages}
                  className="rounded-lg border border-[#DFE5ED] px-3 py-1 font-semibold disabled:opacity-40 dark:border-white/10"
                >
                  Siguiente
                </button>
              </div>
            </div>
          </>
        )}
      </Section>
    </div>
  );
}

function EstadoChip({ estado }: { estado: string }) {
  const meta = estadoMeta(estado);
  return (
    <span
      className="inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-semibold"
      style={{ background: `${meta.color}1A`, color: meta.color }}
      title={meta.hint}
    >
      <span aria-hidden="true" className="h-1.5 w-1.5 rounded-full" style={{ background: meta.color }} />
      {meta.label}
    </span>
  );
}

function pctOf(part: number, total: number): string {
  if (total === 0) return "Sin base para calcular";
  return `${((part / total) * 100).toFixed(1).replace(".", ",")} % de lo recibido`;
}

/** Descarga de un archivo generado. Se revoca la URL para no dejar el blob retenido toda la sesión. */
function download(content: BlobPart, fileName: string, mime: string): void {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}
