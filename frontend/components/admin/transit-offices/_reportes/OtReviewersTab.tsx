"use client";

// «Revisores»: qué hizo cada persona del organismo en un periodo.
//
// El universo son las DECISIONES del rango, no los trámites recibidos — la diferencia con el informe
// del periodo, y la que corresponde a la pregunta. Un trámite radicado hace tres meses y aprobado
// ayer es trabajo de ayer.
//
// Este informe habla de personas, así que se construyó con un sesgo deliberado: el volumen nunca
// aparece solo. Cada tarjeta y cada vista rápida llevan al lado un indicador de calidad o de tiempo,
// porque un ranking de «quién decidió más» es exactamente el instrumento que empuja a decidir rápido
// y mal.

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  fetchOtReviewers,
  OT_REVIEWER_SORT,
  type OtClientCompanyOption,
  type OtReviewerOption,
  type OtReviewerRow,
  type OtReviewerSort,
  type OtReviewersReport,
} from "@/lib/api/ot-metrics";
import { XLSX_MIME } from "@/lib/xlsx";
import { ColumnPicker } from "@/components/consultas/ColumnPicker";
import { activePreset } from "@/components/consultas/columns";
import {
  DateRangeFields,
  EmpresaSelect,
  ModalidadSelect,
  RangePresets,
  defaultRange,
  type DateRange,
} from "./filters";
import { formatHours, formatInt, plural } from "./report-columns";
import { ReviewerPicker } from "./ReviewerPicker";
import {
  buildReviewersCsv,
  buildReviewersXlsx,
  defaultVisibleReviewerColumns,
  REVIEWER_COLUMNS,
  REVIEWER_PRESETS,
  reviewersFileName,
} from "./reviewer-columns";
import { CSV_EXPORT_VISIBLE, Empty, ErrorNotice, PrimaryButton, Section, Tile } from "./shared";
import {
  CARDLIST_CELL,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";

const COLUMNS_STORAGE_KEY = "flit-ot-revisores-columnas";

export interface OtReviewersTabProps {
  transitOfficeId: string;
  companies: OtClientCompanyOption[];
  reviewers: OtReviewerOption[];
}

export function OtReviewersTab({ transitOfficeId, companies, reviewers }: OtReviewersTabProps) {
  const [range, setRange] = useState<DateRange>(() => defaultRange());
  const [modalidad, setModalidad] = useState("");
  const [clientTenantId, setClientTenantId] = useState("");
  const [userIds, setUserIds] = useState<string[]>([]);

  const [sortBy, setSortBy] = useState<OtReviewerSort>(OT_REVIEWER_SORT.decididos);
  const [desc, setDesc] = useState(true);

  const [visibleColumns, setVisibleColumns] = useState<string[]>(defaultVisibleReviewerColumns);
  const [report, setReport] = useState<OtReviewersReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exportState, setExportState] = useState<{ busy: boolean; notice: string | null }>({
    busy: false,
    notice: null,
  });

  // La preferencia de columnas se recupera DESPUÉS del montaje: leer localStorage durante el render
  // haría que el servidor y el cliente pintaran tablas distintas.
  useEffect(() => {
    try {
      const raw = window.localStorage.getItem(COLUMNS_STORAGE_KEY);
      if (!raw) return;
      const parsed: unknown = JSON.parse(raw);
      if (!Array.isArray(parsed)) return;
      const known = parsed.filter(
        (id): id is string => typeof id === "string" && REVIEWER_COLUMNS.some((c) => c.id === id),
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
      userIds,
    }),
    [range.from, range.to, modalidad, clientTenantId, transitOfficeId, userIds],
  );

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setBusy(true);
      setError(null);
      try {
        const data = await fetchOtReviewers({ ...params, sortBy, desc }, signal);
        if (signal?.aborted) return;
        setReport(data);
      } catch (e: unknown) {
        if (signal?.aborted) return;
        setError(e instanceof Error ? e.message : "No se pudo generar el informe de revisores.");
      } finally {
        if (!signal?.aborted) setBusy(false);
      }
    },
    [params, sortBy, desc],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: patrón del repo, skeleton inmediato antes del fetch
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const toggleSort = useCallback(
    (column: OtReviewerSort) => {
      if (column === sortBy) {
        setDesc((v) => !v);
        return;
      }
      setSortBy(column);
      setDesc(true);
    },
    [sortBy],
  );

  // El informe de revisores no pagina: un organismo tiene decenas de personas, no miles, y partirlo
  // en páginas obligaría a encadenar peticiones para exportar algo que ya está entero en memoria.
  const handleExport = useCallback(
    (formato: "xlsx" | "csv") => {
      const filas = report?.filas ?? [];
      if (filas.length === 0) return;

      setExportState({ busy: true, notice: null });
      try {
        const fileName = reviewersFileName(range.from, range.to, formato);
        if (formato === "xlsx") {
          download(buildReviewersXlsx(filas, visibleColumns), fileName, XLSX_MIME);
        } else {
          download(buildReviewersCsv(filas, visibleColumns), fileName, "text/csv;charset=utf-8;");
        }

        setExportState({
          busy: false,
          notice: `Se exportaron ${plural(filas.length, "revisor", "revisores")} con ${plural(
            visibleColumns.length,
            "columna visible",
            "columnas visibles",
          )}.`,
        });
      } catch (e: unknown) {
        setExportState({
          busy: false,
          notice: e instanceof Error ? `No se pudo exportar: ${e.message}` : "No se pudo exportar.",
        });
      }
    },
    [report, visibleColumns, range.from, range.to],
  );

  const resumen = report?.resumen;
  const columns = REVIEWER_COLUMNS.filter((c) => visibleColumns.includes(c.id));
  const presetActivo = activePreset(REVIEWER_PRESETS, visibleColumns);
  const sinFilas = !report || report.filas.length === 0;

  return (
    <div className="flex flex-col gap-6" data-testid="ot-reviewers-tab">
      <Section
        title="Parámetros del informe"
        testId="ot-reviewers-filters"
        hint="Cuenta las DECISIONES tomadas dentro del rango, no los trámites recibidos: un trámite radicado hace meses y aprobado ayer es trabajo de ayer."
      >
        <RangePresets range={range} onChange={setRange} />
        <div className="flex flex-wrap items-end gap-3">
          <DateRangeFields range={range} onChange={setRange} />
          <ReviewerPicker selected={userIds} options={reviewers} onChange={setUserIds} />
          <ModalidadSelect value={modalidad} onChange={setModalidad} />
          <EmpresaSelect value={clientTenantId} companies={companies} onChange={setClientTenantId} />
          <PrimaryButton onClick={() => void load()} disabled={busy}>
            {busy ? "Generando…" : "Actualizar"}
          </PrimaryButton>
        </div>
      </Section>

      {error && <ErrorNotice message={error} />}

      {resumen && resumen.revisores > 0 && (
        <>
          <Section
            title="El equipo en conjunto"
            testId="ot-reviewers-resumen"
            hint="La mediana y el p90 se calculan sobre TODAS las decisiones, no promediando las de cada persona: promediar medianas le da el mismo peso a quien decidió tres casos que a quien decidió trescientos."
          >
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
              <Tile
                value={formatInt(resumen.revisores)}
                label={resumen.revisores === 1 ? "Revisor con actividad" : "Revisores con actividad"}
                hint="Decidieron algo en el periodo"
              />
              <Tile
                value={formatInt(resumen.decididos)}
                label="Trámites gestionados"
                accent="#557EFF"
                hint={`${formatInt(resumen.aprobados)} aprobados · ${formatInt(resumen.rechazados)} rechazados`}
              />
              <Tile
                value={`${resumen.aprobacionPct.toFixed(1).replace(".", ",")} %`}
                label="Aprobación del equipo"
                accent="#8CC63F"
              />
              <Tile
                value={formatHours(resumen.tiempoMedianoHoras)}
                label="Tiempo mediano"
                hint={`p90: ${formatHours(resumen.tiempoP90Horas)}`}
              />
              <Tile
                value={`${resumen.concentracionTopPct.toFixed(1).replace(".", ",")} %`}
                label="Concentración"
                accent={resumen.concentracionTopPct >= 60 ? "#F9AC00" : undefined}
                hint={
                  resumen.revisorMasActivo
                    ? `Se lo lleva ${resumen.revisorMasActivo}`
                    : "Del total lo decide una sola persona"
                }
              />
            </div>

            {/* Un equipo donde una persona hace más de la mitad no es un equipo: es un cuello de
                botella con testigos. El número solo no lo dice; la frase sí. */}
            {resumen.concentracionTopPct >= 60 && resumen.revisores > 1 && (
              <p className="rounded-xl bg-amber-50 px-3 py-2 text-[11px] text-amber-800 dark:bg-amber-500/10 dark:text-amber-300">
                {resumen.revisorMasActivo} concentra el{" "}
                {resumen.concentracionTopPct.toFixed(1).replace(".", ",")} % de las decisiones del
                periodo. Si esa persona falta, la cola se detiene.
              </p>
            )}
          </Section>

          <Section
            title="Reparto del trabajo"
            testId="ot-reviewers-reparto"
            hint="Cada barra lleva su desenlace: el largo dice cuánto, el color dice en qué acabó."
          >
            <ReviewerLoadChart filas={report!.filas} />
          </Section>
        </>
      )}

      <Section
        title="Detalle por revisor"
        testId="ot-reviewers-tabla"
        hint="Elige las columnas que quieres ver. Lo que exportes será exactamente esto."
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <ColumnPicker
              visible={visibleColumns}
              onChange={applyColumns}
              columns={REVIEWER_COLUMNS}
              testId="ot-reviewers-column-picker"
            />
            <button
              type="button"
              onClick={() => handleExport("xlsx")}
              disabled={exportState.busy || sinFilas}
              aria-busy={exportState.busy}
              className="rounded-xl px-3 py-2 text-xs font-semibold text-white transition disabled:opacity-60"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
              data-testid="ot-reviewers-export-xlsx"
            >
              {exportState.busy ? "Exportando…" : "Exportar a Excel"}
            </button>
            {CSV_EXPORT_VISIBLE && (
              <button
                type="button"
                onClick={() => handleExport("csv")}
                disabled={exportState.busy || sinFilas}
                className="rounded-xl border border-[#DFE5ED] px-3 py-2 text-xs font-semibold transition hover:border-[#557EFF] disabled:opacity-60 dark:border-white/10"
                data-testid="ot-reviewers-export-csv"
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
          {REVIEWER_PRESETS.map((preset) => {
            const activa = preset.id === presetActivo;
            return (
              <button
                key={preset.id}
                type="button"
                title={preset.hint}
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
          {presetActivo === null && (
            <span
              className="rounded-full bg-[#F5F7FA] px-3 py-1 text-[11px] font-semibold text-[#6B7280] dark:bg-white/5 dark:text-white/50"
              data-testid="ot-reviewers-preset-personalizada"
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

        {sinFilas ? (
          <Empty>
            {busy
              ? "Generando el informe…"
              : userIds.length > 0
                ? "Los revisores seleccionados no decidieron ningún trámite en el periodo y con los filtros elegidos."
                : "Nadie decidió trámites en el periodo y con los filtros seleccionados."}
          </Empty>
        ) : (
          <div className={CARDLIST_SCROLL}>
            <table className={`min-w-[40rem] ${CARDLIST_TABLE}`} data-testid="ot-reviewers-table">
              <thead>
                <tr className={CARDLIST_HEAD_ROW}>
                  {columns.map((column) => (
                    <th key={column.id} className={CARDLIST_TH}>
                      {column.sort ? (
                        <button
                          type="button"
                          onClick={() => toggleSort(column.sort!)}
                          className="flex items-center gap-1 uppercase transition hover:text-[#557EFF]"
                          aria-label={`Ordenar por ${column.label}`}
                        >
                          {column.label}
                          {sortBy === column.sort && <span aria-hidden="true">{desc ? "↓" : "↑"}</span>}
                        </button>
                      ) : (
                        column.label
                      )}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {report!.filas.map((row) => (
                  <tr key={row.userId} className={CARDLIST_ROW}>
                    {columns.map((column) => (
                      <td
                        key={column.id}
                        className={`${CARDLIST_CELL} ${column.numeric ? "tabular-nums" : ""}`}
                      >
                        {column.value(row)}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Section>
    </div>
  );
}

/**
 * Reparto del trabajo entre las personas del periodo.
 *
 * Barra apilada por persona en vez de una barra por volumen: el largo responde «cuánto» y el color
 * responde «en qué acabó», que es la pregunta que sigue inmediatamente. Con una sola barra gris,
 * quien aprueba todo y quien rechaza todo se dibujan igual.
 */
function ReviewerLoadChart({ filas }: { filas: OtReviewerRow[] }) {
  const max = Math.max(1, ...filas.map((f) => f.decididos));

  return (
    <div className="flex flex-col gap-2" data-testid="ot-reviewers-reparto-chart">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px]">
        <span className="flex items-center gap-1.5">
          <span aria-hidden="true" className="h-2.5 w-2.5 rounded-sm" style={{ background: "#8CC63F" }} />
          Aprobados
        </span>
        <span className="flex items-center gap-1.5">
          <span aria-hidden="true" className="h-2.5 w-2.5 rounded-sm" style={{ background: "#FF4E00" }} />
          Rechazados
        </span>
      </div>

      {filas.map((fila) => (
        <div
          key={fila.userId}
          className="grid grid-cols-[minmax(8rem,12rem)_1fr_auto] items-center gap-3 text-xs"
        >
          <span className="truncate" title={fila.displayName}>
            {fila.displayName}
          </span>
          <span
            className="flex h-2.5 overflow-hidden rounded bg-[#EEF1F5] dark:bg-white/10"
            role="img"
            aria-label={`${fila.displayName}: ${fila.aprobados} aprobados y ${fila.rechazados} rechazados`}
          >
            <span
              className="h-full"
              style={{ width: `${(fila.aprobados / max) * 100}%`, background: "#8CC63F" }}
            />
            <span
              className="h-full"
              style={{ width: `${(fila.rechazados / max) * 100}%`, background: "#FF4E00" }}
            />
          </span>
          <span className="tabular-nums font-semibold">{formatInt(fila.decididos)}</span>
        </div>
      ))}
    </div>
  );
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
