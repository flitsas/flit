"use client";

// «Consultas»: el usuario arma su propia búsqueda sobre los trámites, la guarda y la exporta.
//
// Se abre con la tabla YA poblada y los filtros como fichas encima. Es lo contrario del constructor
// clásico —pantalla aparte, campo/operador/valor, botón «Ejecutar»—, y la diferencia no es estética:
// aquel se entra en blanco, y ante un lienzo en blanco la gente no sabe qué preguntar. Aquí se
// empieza por un resultado, se va acotando viendo bajar el contador, y «guardar» solo congela el
// estado al que se llegó.
//
// El catálogo de campos lo sirve el backend. Esta pantalla no sabe qué campos existen: un campo
// consultable nuevo aparece aquí sin desplegar frontend.

import { useCallback, useEffect, useState } from "react";
import {
  deleteOtSavedQuery,
  fetchOtQueryFields,
  fetchOtSavedQueries,
  runOtQuery,
  saveOtQuery,
  DATE_FIELD_LABEL,
  OT_QUERY_MAX_PAGE_SIZE,
  RANGE_PRESETS,
  type OtQueryCondition,
  type OtQueryDateFieldId,
  type OtQueryDefinition,
  type OtQueryField,
  type OtQueryRangePreset,
  type OtQueryResult,
  type OtSavedQuery,
} from "@/lib/api/ot-queries";
import { XLSX_MIME } from "@/lib/xlsx";
import { ColumnPicker } from "./ColumnPicker";
import { activePreset } from "./columns";
import { CoverageNotice, coverageLines } from "./CoverageNotice";
import { estadoMeta, formatInt, plural } from "./report-columns";
import { QueryFilterBar } from "./QueryFilterBar";
import {
  buildQueryCsv,
  buildQueryXlsx,
  defaultQueryColumns,
  QUERY_COLUMNS,
  QUERY_PRESETS,
  queryFileName,
} from "./query-columns";
import { SavedQueryList } from "./SavedQueryList";
import { Empty, ErrorNotice, FIELD_CLS, PrimaryButton, Section } from "./shared";

const COLUMNS_STORAGE_KEY = "flit-ot-consultas-columnas";
const PAGE_SIZE = 25;

/** Tope de filas del export. Se AVISA cuando recorta: un archivo truncado en silencio se firma. */
const MAX_EXPORT_ROWS = 5000;

function emptyDefinition(): OtQueryDefinition {
  return {
    fechas: { campo: "radicacion", preset: "ultimos_30" },
    condiciones: [],
    columnas: [],
  };
}

export interface OtQueriesTabProps {
  transitOfficeId: string;
}

export function OtQueriesTab({ transitOfficeId }: OtQueriesTabProps) {
  const [fields, setFields] = useState<OtQueryField[]>([]);
  const [saved, setSaved] = useState<OtSavedQuery[]>([]);
  const [definition, setDefinition] = useState<OtQueryDefinition>(emptyDefinition);
  const [visibleColumns, setVisibleColumns] = useState<string[]>(defaultQueryColumns);

  const [result, setResult] = useState<OtQueryResult | null>(null);
  const [page, setPage] = useState(1);
  const [busy, setBusy] = useState(false);
  // El export tiene su propio estado: compartirlo con el contador dejaría el botón inservible cada
  // vez que se refresca la cifra, que es todo el rato mientras alguien ajusta los filtros.
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [activeId, setActiveId] = useState<string | null>(null);
  const [activeSnapshot, setActiveSnapshot] = useState<string | null>(null);
  // Nombrar y confirmar se hacen dentro de la página. `window.prompt` y `window.confirm` son
  // rápidos de escribir y peores en todo lo demás: bloquean la pestaña entera, no se pueden
  // estilar, y en algunos navegadores un usuario que marca «no volver a mostrar» los deja mudos
  // para siempre — el botón dejaría de funcionar sin decir nada.
  const [nombrando, setNombrando] = useState<string | null>(null);
  const [porBorrar, setPorBorrar] = useState<OtSavedQuery | null>(null);

  // La preferencia de columnas se rehidrata DESPUÉS del montaje: leer localStorage durante el
  // render haría que el servidor y el cliente pintaran tablas distintas.
  useEffect(() => {
    try {
      const raw = window.localStorage.getItem(COLUMNS_STORAGE_KEY);
      if (!raw) return;
      const parsed: unknown = JSON.parse(raw);
      if (!Array.isArray(parsed)) return;
      const known = parsed.filter(
        (id): id is string => typeof id === "string" && QUERY_COLUMNS.some((c) => c.id === id),
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

  // Catálogo y consultas guardadas: una sola vez. Cada uno con su propio catch para que la caída de
  // uno no deje la pantalla sin el otro — sin campos no se puede filtrar, pero la tabla sirve igual.
  useEffect(() => {
    const controller = new AbortController();

    void fetchOtQueryFields(transitOfficeId, controller.signal)
      .then((data) => !controller.signal.aborted && setFields(data))
      .catch(() => undefined);

    void fetchOtSavedQueries(transitOfficeId, controller.signal)
      .then((data) => !controller.signal.aborted && setSaved(data))
      .catch(() => undefined);

    return () => controller.abort();
  }, [transitOfficeId]);

  // El enlace compartible lleva la consulta dentro. Pegar la dirección en el chat abre exactamente
  // el mismo resultado, que es como estos informes circulan de verdad.
  useEffect(() => {
    const encoded = new URLSearchParams(window.location.search).get("q");
    if (!encoded) return;
    const decoded = decodeDefinition(encoded);
    // eslint-disable-next-line react-hooks/set-state-in-effect -- estado inicial desde la URL: solo existe en el cliente
    if (decoded) setDefinition(decoded);
  }, []);

  const definitionKey = JSON.stringify(definition);

  /**
   * La huella de la PREGUNTA: fechas y condiciones, no columnas ni orden.
   *
   * Es lo que decide si lo de pantalla se apartó de la consulta guardada. Incluir las columnas
   * marcaría «modificada» por enseñar una columna más, que no cambia ni una fila del resultado —
   * y, peor, la marcaría nada más guardar, porque el servidor devuelve la definición con los
   * huecos ya rellenos.
   */
  const preguntaKey = JSON.stringify([definition.fechas, definition.condiciones]);

  const load = useCallback(
    async (signal?: AbortSignal, targetPage = 1) => {
      setBusy(true);
      setError(null);
      try {
        const data = await runOtQuery(definition, {
          page: targetPage,
          pageSize: PAGE_SIZE,
          transitOfficeId,
          signal,
        });
        if (signal?.aborted) return;
        setResult(data);
      } catch (e: unknown) {
        if (signal?.aborted) return;
        setError(e instanceof Error ? e.message : "No se pudo ejecutar la consulta.");
      } finally {
        if (!signal?.aborted) setBusy(false);
      }
    },
    // La clave serializada evita relanzar por una identidad de objeto nueva con el mismo contenido.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [definitionKey, transitOfficeId],
  );

  // Un respiro antes de consultar: el contador se actualiza mientras se marcan casillas, y sin esto
  // cada clic dispararía su propia petición y ganaría la que volviera última, no la última hecha.
  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => void load(controller.signal, 1), 250);
    // eslint-disable-next-line react-hooks/set-state-in-effect -- cambiar los filtros invalida la página: quedarse en la 4 de un resultado que ahora tiene 2 dejaría la tabla vacía sin motivo
    setPage(1);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [load]);

  const changePage = useCallback(
    (next: number) => {
      setPage(next);
      void load(undefined, next);
    },
    [load],
  );

  const setCondiciones = useCallback((condiciones: OtQueryCondition[]) => {
    setDefinition((prev) => ({ ...prev, condiciones }));
  }, []);

  const openSaved = useCallback((query: OtSavedQuery) => {
    setDefinition(query.definition);
    setActiveId(query.id);
    setActiveSnapshot(JSON.stringify([query.definition.fechas, query.definition.condiciones]));
    setNotice(null);
    if (query.definition.columnas.length > 0) {
      setVisibleColumns(query.definition.columnas.filter((id) => QUERY_COLUMNS.some((c) => c.id === id)));
    }
  }, []);

  const sugerido = saved.find((q) => q.id === activeId && !q.deFabrica)?.nombre ?? "";

  const handleSave = useCallback(async (nombre: string) => {
    if (!nombre.trim()) return;

    try {
      const guardada = await saveOtQuery(
        {
          // Guardar sobre una de fábrica la duplica: no vive en la base y tiene que seguir ahí.
          // Y guardar con OTRO nombre también duplica: renombrar en el sitio es un caso distinto
          // del de sacar una variante, y confundirlos hace perder la consulta original.
          id: nombre.trim() === sugerido ? (activeId ?? undefined) : undefined,
          nombre: nombre.trim(),
          definition: { ...definition, columnas: visibleColumns },
        },
        transitOfficeId,
      );

      setSaved((prev) => [guardada, ...prev.filter((q) => q.id !== guardada.id)]);
      setActiveId(guardada.id);
      setActiveSnapshot(
        JSON.stringify([guardada.definition.fechas, guardada.definition.condiciones]),
      );
      setNombrando(null);
      setNotice(`Consulta «${guardada.nombre}» guardada.`);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo guardar la consulta.");
    }
  }, [activeId, definition, sugerido, transitOfficeId, visibleColumns]);

  const handleDelete = useCallback(
    async (query: OtSavedQuery) => {
      try {
        await deleteOtSavedQuery(query.id, transitOfficeId);
        setSaved((prev) => prev.filter((q) => q.id !== query.id));
        setPorBorrar(null);
        if (activeId === query.id) {
          setActiveId(null);
          setActiveSnapshot(null);
        }
      } catch (e: unknown) {
        setError(e instanceof Error ? e.message : "No se pudo borrar la consulta.");
      }
    },
    [activeId, transitOfficeId],
  );

  const handleCopyLink = useCallback(async () => {
    const url = new URL(window.location.href);
    url.searchParams.set("q", encodeDefinition(definition));
    try {
      await navigator.clipboard.writeText(url.toString());
      setNotice("Enlace copiado. Quien lo abra verá esta misma consulta.");
    } catch {
      setNotice(url.toString());
    }
  }, [definition]);

  /**
   * El export recorre TODAS las páginas, no la visible.
   *
   * Es la pregunta que hace todo el mundo la primera vez, y la respuesta contraria —exportar los 25
   * de pantalla— sería una trampa: el archivo parecería completo y nadie lo comprobaría.
   */
  const handleExport = useCallback(
    async (formato: "xlsx" | "csv") => {
      if (!result || result.total === 0) return;

      setExporting(true);
      setNotice(null);
      try {
        const filas = [];
        let pagina = 1;
        while (filas.length < Math.min(result.total, MAX_EXPORT_ROWS)) {
          const data = await runOtQuery(definition, {
            page: pagina,
            pageSize: OT_QUERY_MAX_PAGE_SIZE,
            transitOfficeId,
          });
          if (data.filas.length === 0) break;
          filas.push(...data.filas);
          pagina += 1;
        }

        const recortado = filas.length > MAX_EXPORT_ROWS ? filas.slice(0, MAX_EXPORT_ROWS) : filas;
        const notas = coverageLines(result.cobertura);
        if (recortado.length < result.total) {
          notas.push(
            `Atención: se exportaron ${recortado.length} de ${result.total} trámites (tope de ${MAX_EXPORT_ROWS}). Acote la consulta para obtener el resto.`,
          );
        }

        const nombre = saved.find((q) => q.id === activeId)?.nombre ?? null;
        const fileName = queryFileName(nombre, result.desde, result.hasta, formato);

        if (formato === "xlsx") {
          download(buildQueryXlsx(recortado, visibleColumns, notas), fileName, XLSX_MIME);
        } else {
          download(
            buildQueryCsv(recortado, visibleColumns, notas),
            fileName,
            "text/csv;charset=utf-8;",
          );
        }

        setNotice(
          `Se exportaron ${plural(recortado.length, "trámite", "trámites")} con ${plural(
            visibleColumns.length,
            "columna visible",
            "columnas visibles",
          )}.`,
        );
      } catch (e: unknown) {
        setError(e instanceof Error ? `No se pudo exportar: ${e.message}` : "No se pudo exportar.");
      } finally {
        setExporting(false);
      }
    },
    [activeId, definition, result, saved, transitOfficeId, visibleColumns],
  );

  const columns = QUERY_COLUMNS.filter((c) => visibleColumns.includes(c.id));
  const presetActivo = activePreset(QUERY_PRESETS, visibleColumns);
  const modificada = activeSnapshot !== null && activeSnapshot !== preguntaKey;
  const totalPaginas = result ? Math.max(1, Math.ceil(result.total / PAGE_SIZE)) : 1;

  return (
    <div className="flex flex-col gap-6 lg:flex-row" data-testid="ot-queries-tab">
      <aside className="lg:w-56 lg:shrink-0">
        <Section title="Consultas" testId="ot-query-lista">
          <SavedQueryList
            queries={saved}
            activeId={activeId}
            modificada={modificada}
            porBorrar={porBorrar}
            onOpen={openSaved}
            onPedirBorrado={setPorBorrar}
            onConfirmarBorrado={(q) => void handleDelete(q)}
          />
          <button
            type="button"
            onClick={() => {
              setDefinition(emptyDefinition());
              setActiveId(null);
              setActiveSnapshot(null);
            }}
            className="mt-2 w-full rounded-lg border border-dashed border-[#DFE5ED] px-2 py-1.5 text-[11px] font-semibold text-[#6B7280] hover:border-[#557EFF] hover:text-[#557EFF] dark:border-white/20 dark:text-white/60"
            data-testid="ot-query-nueva"
          >
            Empezar de cero
          </button>
        </Section>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col gap-4">
        <Section
          title="Qué quiere ver"
          testId="ot-query-filtros-seccion"
          hint="Los filtros se combinan con «y». Dentro de un mismo campo, varios valores se combinan con «o» — por eso una lista de placas funciona pegándola tal cual."
        >
          <div className="flex flex-wrap items-end gap-3">
            <label className="flex flex-col gap-1 text-xs font-semibold">
              Rango sobre
              <select
                value={definition.fechas.campo}
                onChange={(e) =>
                  setDefinition((prev) => ({
                    ...prev,
                    fechas: { ...prev.fechas, campo: e.target.value as OtQueryDateFieldId },
                  }))
                }
                className={FIELD_CLS}
                data-testid="ot-query-campo-fecha"
              >
                {(Object.keys(DATE_FIELD_LABEL) as OtQueryDateFieldId[]).map((campo) => (
                  <option key={campo} value={campo}>
                    {DATE_FIELD_LABEL[campo]}
                  </option>
                ))}
              </select>
            </label>

            <label className="flex flex-col gap-1 text-xs font-semibold">
              Periodo
              <select
                value={definition.fechas.preset}
                onChange={(e) =>
                  setDefinition((prev) => ({
                    ...prev,
                    fechas: { ...prev.fechas, preset: e.target.value as OtQueryRangePreset },
                  }))
                }
                className={FIELD_CLS}
                data-testid="ot-query-preset"
              >
                {RANGE_PRESETS.map((preset) => (
                  <option key={preset.value} value={preset.value}>
                    {preset.label}
                  </option>
                ))}
              </select>
            </label>

            {definition.fechas.preset === "personalizado" && (
              <>
                <DateField
                  label="Desde"
                  value={definition.fechas.from ?? ""}
                  onChange={(from) =>
                    setDefinition((prev) => ({ ...prev, fechas: { ...prev.fechas, from } }))
                  }
                />
                <DateField
                  label="Hasta"
                  value={definition.fechas.to ?? ""}
                  onChange={(to) =>
                    setDefinition((prev) => ({ ...prev, fechas: { ...prev.fechas, to } }))
                  }
                />
              </>
            )}
          </div>

          <QueryFilterBar
            fields={fields}
            conditions={definition.condiciones}
            onChange={setCondiciones}
          />
        </Section>

        {error && <ErrorNotice message={error} />}
        {result && <CoverageNotice cobertura={result.cobertura} />}

        <Section title="Resultado" testId="ot-query-resultado">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="text-2xl font-semibold tabular-nums" data-testid="ot-query-total">
                {busy && !result ? "…" : formatInt(result?.total ?? 0)}{" "}
                <span className="text-sm font-normal text-[#6B7280] dark:text-white/50">
                  {result?.total === 1 ? "trámite" : "trámites"}
                </span>
              </p>
              {result && <Comparacion result={result} />}
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <ColumnPicker
                visible={visibleColumns}
                onChange={applyColumns}
                columns={QUERY_COLUMNS}
                testId="ot-query-column-picker"
              />
              <button
                type="button"
                onClick={() => void handleCopyLink()}
                className="rounded-lg border border-[#DFE5ED] px-3 py-1.5 text-xs font-semibold text-[#6B7280] hover:text-[#557EFF] dark:border-white/15 dark:text-white/60"
                data-testid="ot-query-enlace"
              >
                Copiar enlace
              </button>
              <PrimaryButton onClick={() => setNombrando(sugerido)} disabled={exporting}>
                Guardar consulta
              </PrimaryButton>
              <button
                type="button"
                onClick={() => void handleExport("xlsx")}
                disabled={exporting || !result || result.total === 0}
                className="rounded-lg bg-[#1D6F42] px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-40"
                data-testid="ot-query-export-xlsx"
              >
                {exporting ? "Exportando…" : "Exportar a Excel"}
              </button>
              <button
                type="button"
                onClick={() => void handleExport("csv")}
                disabled={exporting || !result || result.total === 0}
                className="rounded-lg border border-[#DFE5ED] px-3 py-1.5 text-xs font-semibold text-[#6B7280] disabled:opacity-40 dark:border-white/15 dark:text-white/60"
                data-testid="ot-query-export-csv"
              >
                CSV
              </button>
            </div>
          </div>

          {nombrando !== null && (
            <form
              className="flex flex-wrap items-center gap-2 rounded-xl bg-[#F5F7FA] p-2 dark:bg-white/5"
              onSubmit={(e) => {
                e.preventDefault();
                void handleSave(nombrando);
              }}
              data-testid="ot-query-nombrar"
            >
              <label className="text-xs font-semibold" htmlFor="ot-query-nombre">
                Nombre
              </label>
              <input
                id="ot-query-nombre"
                autoFocus
                value={nombrando}
                onChange={(e) => setNombrando(e.target.value)}
                placeholder="Traspasos con prenda de agosto"
                className={`${FIELD_CLS} min-w-[16rem] flex-1`}
                data-testid="ot-query-nombre-input"
              />
              <button
                type="submit"
                disabled={!nombrando.trim()}
                className="rounded-lg bg-[#557EFF] px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-40"
                data-testid="ot-query-guardar-confirmar"
              >
                Guardar
              </button>
              <button
                type="button"
                onClick={() => setNombrando(null)}
                className="text-[11px] font-semibold text-[#6B7280] dark:text-white/50"
              >
                Cancelar
              </button>
            </form>
          )}

          <div className="flex flex-wrap gap-1.5">
            {QUERY_PRESETS.map((preset) => (
              <button
                key={preset.id}
                type="button"
                onClick={() => applyColumns(preset.columns)}
                aria-pressed={presetActivo === preset.id}
                title={preset.hint}
                className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold ${
                  presetActivo === preset.id
                    ? "border-[#557EFF] bg-[#557EFF]/10 text-[#3355CC] dark:text-[#9DB5FF]"
                    : "border-[#DFE5ED] text-[#6B7280] hover:text-[#557EFF] dark:border-white/15 dark:text-white/60"
                }`}
              >
                {preset.label}
              </button>
            ))}
          </div>

          {notice && (
            <p className="text-[11px] text-[#6B7280] dark:text-white/50" role="status">
              {notice}
            </p>
          )}

          {!result || result.filas.length === 0 ? (
            <Empty>
              {busy
                ? "Consultando…"
                : "Ningún trámite cumple estos filtros. Quite alguna ficha o amplíe el periodo."}
            </Empty>
          ) : (
            <>
              <div className="overflow-x-auto">
                <table className="w-full min-w-[48rem] border-collapse text-xs">
                  <thead>
                    <tr className="border-b border-[#DFE5ED] text-left dark:border-white/10">
                      {columns.map((column) => (
                        <th
                          key={column.id}
                          scope="col"
                          className={`px-2 py-2 font-semibold ${column.numeric ? "text-right" : ""}`}
                        >
                          {column.label}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {result.filas.map((fila) => (
                      <tr
                        key={fila.procedureInstanceId}
                        className="border-b border-[#EEF1F5] last:border-0 dark:border-white/5"
                      >
                        {columns.map((column) => (
                          <td
                            key={column.id}
                            className={`px-2 py-1.5 ${column.numeric ? "text-right tabular-nums" : ""}`}
                          >
                            {column.id === "estado" ? (
                              <span
                                className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
                                style={{
                                  background: `${estadoMeta(fila.estadoOt).color}1A`,
                                  color: estadoMeta(fila.estadoOt).color,
                                }}
                              >
                                {estadoMeta(fila.estadoOt).label}
                              </span>
                            ) : (
                              column.value(fila)
                            )}
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {totalPaginas > 1 && (
                <div className="flex items-center justify-between gap-3 text-xs">
                  <span className="text-[#6B7280] dark:text-white/50">
                    Página {page} de {totalPaginas}
                  </span>
                  <span className="flex gap-2">
                    <button
                      type="button"
                      onClick={() => changePage(page - 1)}
                      disabled={page <= 1 || busy}
                      className="rounded-lg border border-[#DFE5ED] px-3 py-1 font-semibold disabled:opacity-40 dark:border-white/15"
                    >
                      Anterior
                    </button>
                    <button
                      type="button"
                      onClick={() => changePage(page + 1)}
                      disabled={page >= totalPaginas || busy}
                      className="rounded-lg border border-[#DFE5ED] px-3 py-1 font-semibold disabled:opacity-40 dark:border-white/15"
                      data-testid="ot-query-siguiente"
                    >
                      Siguiente
                    </button>
                  </span>
                </div>
              )}
            </>
          )}
        </Section>
      </div>
    </div>
  );
}

/**
 * Contra el periodo anterior de igual ancho.
 *
 * Un número suelto no dice si es mucho o poco. Con el anterior al lado, «1.284» pasa a significar
 * algo sin que nadie tenga que ejecutar la consulta dos veces cambiando las fechas a mano.
 */
function Comparacion({ result }: { result: OtQueryResult }) {
  const anterior = result.totalPeriodoAnterior;
  if (anterior === 0) {
    return (
      <p className="text-[11px] text-[#6B7280] dark:text-white/50">
        {result.desde} a {result.hasta} · nada comparable en el periodo previo
      </p>
    );
  }

  const delta = ((result.total - anterior) / anterior) * 100;
  const signo = delta >= 0 ? "+" : "";

  return (
    <p className="text-[11px] text-[#6B7280] dark:text-white/50" data-testid="ot-query-comparacion">
      {result.desde} a {result.hasta} · {signo}
      {delta.toFixed(0)} % frente a los {formatInt(anterior)} del periodo anterior
    </p>
  );
}

function DateField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="flex flex-col gap-1 text-xs font-semibold">
      {label}
      <input
        type="date"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className={FIELD_CLS}
      />
    </label>
  );
}

/**
 * La consulta dentro del enlace.
 *
 * Va en base64 y no como parámetros sueltos porque una lista de cuarenta placas convertiría la
 * dirección en algo que ningún chat respeta al pegarlo. Se codifica a UTF-8 antes para que los
 * nombres con tilde no rompan `btoa`.
 */
function encodeDefinition(definition: OtQueryDefinition): string {
  const json = JSON.stringify(definition);
  const bytes = new TextEncoder().encode(json);
  return btoa(String.fromCharCode(...bytes));
}

function decodeDefinition(encoded: string): OtQueryDefinition | null {
  try {
    const binary = atob(encoded);
    const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
    const parsed: unknown = JSON.parse(new TextDecoder().decode(bytes));
    if (!parsed || typeof parsed !== "object" || !("fechas" in parsed)) return null;
    return parsed as OtQueryDefinition;
  } catch {
    // Un enlace recortado por el chat no debe dejar la pantalla en blanco: se abre la consulta
    // vacía, que es un sitio del que el usuario sabe salir.
    return null;
  }
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
