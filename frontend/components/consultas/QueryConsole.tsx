"use client";

// La consola de consultas: el usuario arma su propia búsqueda sobre los trámites, la guarda y la
// exporta.
//
// Se abre con la tabla YA poblada y los filtros como fichas encima. Es lo contrario del constructor
// clásico —pantalla aparte, campo/operador/valor, botón «Ejecutar»—, y la diferencia no es estética:
// aquel se entra en blanco, y ante un lienzo en blanco la gente no sabe qué preguntar. Aquí se
// empieza por un resultado, se va acotando viendo bajar el contador, y «guardar» solo congela el
// estado al que se llegó.
//
// El catálogo de campos lo sirve el backend. Esta pantalla no sabe qué campos existen: un campo
// consultable nuevo aparece aquí sin desplegar frontend.
//
// La usan DOS módulos —el organismo de tránsito y la empresa gestora— y esa es toda la diferencia
// entre ellos: cada uno trae su catálogo, sus columnas y su cliente de API en un `QuerySource`.
// Todo lo demás —el ritmo de las peticiones, el enlace vivo, la cobertura, el export que recorre
// todas las páginas— es el mismo código, porque son la misma promesa.

import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import {
  QUERY_MAX_PAGE_SIZE,
  RANGE_PRESETS,
  type QueryCondition,
  type QueryDefinition,
  type QueryField,
  type QueryRangePreset,
  type QueryResult,
  type QuerySource,
  type SavedQuery,
} from "@/lib/api/queries";
import { XLSX_MIME } from "@/lib/xlsx";
import { ColumnPicker } from "./ColumnPicker";
import {
  activePreset,
  buildCsv,
  buildWorkbook,
  type ColumnPreset,
  type DataColumn,
} from "./columns";
import { CoverageNotice, coverageLines } from "./CoverageNotice";
import { download, queryFileName } from "./export";
import { decodeDefinition, encodeDefinition } from "./link-state";
import { QueryFilterBar } from "./QueryFilterBar";
import { SavedQueryList } from "./SavedQueryList";
import { formatInt, plural } from "./format";
import {
  CSV_EXPORT_VISIBLE,
  Empty,
  ErrorNotice,
  FIELD_CLS,
  PrimaryButton,
  Section,
} from "./ui";

const PAGE_SIZE = 25;

/** Tope de filas del export. Se AVISA cuando recorta: un archivo truncado en silencio se firma. */
const MAX_EXPORT_ROWS = 5000;

export interface QueryConsoleProps<TRow> {
  source: QuerySource<TRow>;
  columns: DataColumn<TRow>[];
  presets: ColumnPreset[];
  defaultColumns: string[];
  rowKey: (row: TRow) => string;
  /** Nombre de la hoja del libro de Excel. */
  sheetName: string;
  /** Dónde no está lo que no salió, para el aviso de cobertura: «este organismo», «su empresa». */
  ambito: string;
  /**
   * Celdas que se pintan distinto de su texto —una insignia de estado, por ejemplo—. Devolver
   * `null` deja que la columna se pinte como siempre.
   */
  renderCell?: (columnId: string, row: TRow) => ReactNode | null;
  /** `data-testid` de la raíz, si el módulo ya tenía uno con el que se le conoce. */
  rootTestId?: string;
}

export function QueryConsole<TRow>({
  source,
  columns: allColumns,
  presets,
  defaultColumns,
  rowKey,
  sheetName,
  ambito,
  renderCell,
  rootTestId,
}: QueryConsoleProps<TRow>) {
  const prefix = source.testIdPrefix;
  const [singular, pluralNoun] = source.rowNoun;

  const emptyDefinition = useCallback(
    (): QueryDefinition => ({
      fechas: { campo: source.defaultDateField, preset: "ultimos_30" },
      condiciones: [],
      columnas: [],
    }),
    [source.defaultDateField],
  );

  const [fields, setFields] = useState<QueryField[]>([]);
  const [saved, setSaved] = useState<SavedQuery[]>([]);
  const [definition, setDefinition] = useState<QueryDefinition>(emptyDefinition);
  const [visibleColumns, setVisibleColumns] = useState<string[]>(defaultColumns);

  const [result, setResult] = useState<QueryResult<TRow> | null>(null);
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
  const [porBorrar, setPorBorrar] = useState<SavedQuery | null>(null);

  // La preferencia de columnas se rehidrata DESPUÉS del montaje: leer localStorage durante el
  // render haría que el servidor y el cliente pintaran tablas distintas.
  useEffect(() => {
    try {
      const raw = window.localStorage.getItem(source.columnsStorageKey);
      if (!raw) return;
      const parsed: unknown = JSON.parse(raw);
      if (!Array.isArray(parsed)) return;
      const known = parsed.filter(
        (id): id is string => typeof id === "string" && allColumns.some((c) => c.id === id),
      );
      // eslint-disable-next-line react-hooks/set-state-in-effect -- rehidratación de preferencia: no hay otro momento para leer localStorage
      if (known.length > 0) setVisibleColumns(known);
    } catch {
      /* modo privado o JSON corrupto: se sigue con las columnas por defecto */
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [source.columnsStorageKey]);

  const applyColumns = useCallback(
    (ids: string[]) => {
      setVisibleColumns(ids);
      try {
        window.localStorage.setItem(source.columnsStorageKey, JSON.stringify(ids));
      } catch {
        /* la preferencia no se persiste, pero la sesión actual sí funciona */
      }
    },
    [source.columnsStorageKey],
  );

  // Catálogo y consultas guardadas: una sola vez. Cada uno con su propio catch para que la caída de
  // uno no deje la pantalla sin el otro — sin campos no se puede filtrar, pero la tabla sirve igual.
  useEffect(() => {
    const controller = new AbortController();

    void source
      .fetchFields(controller.signal)
      .then((data) => !controller.signal.aborted && setFields(data))
      .catch(() => undefined);

    void source
      .fetchSaved(controller.signal)
      .then((data) => !controller.signal.aborted && setSaved(data))
      .catch(() => undefined);

    return () => controller.abort();
  }, [source]);

  // El enlace compartible lleva la consulta dentro. Pegar la dirección en el chat abre exactamente
  // el mismo resultado, que es como estos informes circulan de verdad.
  useEffect(() => {
    const encoded = new URLSearchParams(window.location.search).get("q");
    if (!encoded) return;
    const decoded = decodeDefinition(encoded);
    // eslint-disable-next-line react-hooks/set-state-in-effect -- estado inicial desde la URL: solo existe en el cliente
    if (decoded) setDefinition(decoded);
  }, []);

  /**
   * …y la dirección se mantiene al día con lo que hay en pantalla.
   *
   * No es solo para compartir. Cada pestaña de la consola se monta y se desmonta, así que sin esto
   * ir a otra pestaña y volver devolvía la consulta al estado del enlace de entrada —o a la vacía—,
   * tirando lo que el usuario llevaba armado. Con la consulta en la dirección, salir y volver, o
   * recargar, la encuentra donde la dejó.
   *
   * `replaceState` porque ajustar un filtro no es navegar, y cada retoque empujando una entrada al
   * historial dejaría el botón «atrás» inservible.
   */
  const definitionKey = JSON.stringify(definition);
  const sincronizado = useRef(false);

  useEffect(() => {
    // La primera pasada se salta: corre junto al efecto de arriba, cuando `definition` es todavía
    // la vacía, y borraría el `?q=` del enlace justo antes de que llegue a aplicarse.
    if (!sincronizado.current) {
      sincronizado.current = true;
      return;
    }
    try {
      const url = new URL(window.location.href);
      const vacia = definition.condiciones.length === 0 && definition.fechas.preset === "ultimos_30";
      // Una consulta sin tocar no ensucia la dirección con un base64 que no dice nada.
      if (vacia) url.searchParams.delete("q");
      else url.searchParams.set("q", encodeDefinition(definition));
      window.history.replaceState(window.history.state, "", url);
    } catch {
      /* entorno sin history: se pierde el enlace vivo, no la consulta */
    }
    // La clave serializada evita reescribir la dirección por una identidad de objeto nueva.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [definitionKey]);

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
        const data = await source.run(definition, {
          page: targetPage,
          pageSize: PAGE_SIZE,
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
    [definitionKey, source],
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

  const setCondiciones = useCallback((condiciones: QueryCondition[]) => {
    setDefinition((prev) => ({ ...prev, condiciones }));
  }, []);

  const openSaved = useCallback(
    (query: SavedQuery) => {
      setDefinition(query.definition);
      setActiveId(query.id);
      setActiveSnapshot(JSON.stringify([query.definition.fechas, query.definition.condiciones]));
      setNotice(null);
      if (query.definition.columnas.length > 0) {
        setVisibleColumns(
          query.definition.columnas.filter((id) => allColumns.some((c) => c.id === id)),
        );
      }
    },
    [allColumns],
  );

  const sugerido = saved.find((q) => q.id === activeId && !q.deFabrica)?.nombre ?? "";

  const handleSave = useCallback(
    async (nombre: string) => {
      if (!nombre.trim()) return;

      try {
        const guardada = await source.save({
          // Guardar sobre una de fábrica la duplica: no vive en la base y tiene que seguir ahí.
          // Y guardar con OTRO nombre también duplica: renombrar en el sitio es un caso distinto
          // del de sacar una variante, y confundirlos hace perder la consulta original.
          id: nombre.trim() === sugerido ? (activeId ?? undefined) : undefined,
          nombre: nombre.trim(),
          definition: { ...definition, columnas: visibleColumns },
        });

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
    },
    [activeId, definition, source, sugerido, visibleColumns],
  );

  const handleDelete = useCallback(
    async (query: SavedQuery) => {
      try {
        await source.remove(query.id);
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
    [activeId, source],
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
        const filas: TRow[] = [];
        let pagina = 1;
        while (filas.length < Math.min(result.total, MAX_EXPORT_ROWS)) {
          const data = await source.run(definition, {
            page: pagina,
            pageSize: QUERY_MAX_PAGE_SIZE,
          });
          if (data.filas.length === 0) break;
          filas.push(...data.filas);
          pagina += 1;
        }

        const recortado = filas.length > MAX_EXPORT_ROWS ? filas.slice(0, MAX_EXPORT_ROWS) : filas;
        const notas = coverageLines(result.cobertura, fields);
        if (recortado.length < result.total) {
          notas.push(
            `Atención: se exportaron ${recortado.length} de ${result.total} ${pluralNoun} (tope de ${MAX_EXPORT_ROWS}). Acote la consulta para obtener el resto.`,
          );
        }

        const nombre = saved.find((q) => q.id === activeId)?.nombre ?? null;
        const fileName = queryFileName(
          source.exportPrefix,
          nombre,
          result.desde,
          result.hasta,
          formato,
        );

        if (formato === "xlsx") {
          download(
            buildWorkbook(sheetName, allColumns, recortado, visibleColumns, notas),
            fileName,
            XLSX_MIME,
          );
        } else {
          const csv = buildCsv(allColumns, recortado, visibleColumns);
          const conNotas =
            notas.length > 0
              ? `${csv}\r\n\r\n${notas.map((n) => `"${n.replace(/"/g, '""')}"`).join("\r\n")}`
              : csv;
          download(conNotas, fileName, "text/csv;charset=utf-8;");
        }

        setNotice(
          `Se exportaron ${plural(recortado.length, singular, pluralNoun)} con ${plural(
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
    [
      activeId,
      allColumns,
      definition,
      fields,
      pluralNoun,
      result,
      saved,
      sheetName,
      singular,
      source,
      visibleColumns,
    ],
  );

  const columns = allColumns.filter((c) => visibleColumns.includes(c.id));
  const presetActivo = activePreset(presets, visibleColumns);
  const modificada = activeSnapshot !== null && activeSnapshot !== preguntaKey;
  const totalPaginas = result ? Math.max(1, Math.ceil(result.total / PAGE_SIZE)) : 1;

  return (
    <div className="flex flex-col gap-6 lg:flex-row" data-testid={rootTestId ?? `${prefix}-tab`}>
      {/* Algo más ancha de lo que pediría una lista de nombres: las tarjetas llevan nombre y
          resumen, y a 14rem el resumen se corta justo donde empieza a ser útil. */}
      <aside className="lg:w-64 lg:shrink-0">
        <Section title="Consultas" testId={`${prefix}-lista`}>
          <SavedQueryList
            queries={saved}
            activeId={activeId}
            modificada={modificada}
            porBorrar={porBorrar}
            onOpen={openSaved}
            onPedirBorrado={setPorBorrar}
            onConfirmarBorrado={(q) => void handleDelete(q)}
            testIdPrefix={prefix}
          />
          <button
            type="button"
            onClick={() => {
              setDefinition(emptyDefinition());
              setActiveId(null);
              setActiveSnapshot(null);
            }}
            className="mt-1 w-full rounded-xl border border-dashed border-[#DFE5ED] px-2 py-2 text-[11px] font-semibold text-[#6B7280] transition hover:border-[#557EFF] hover:text-[#557EFF] dark:border-white/20 dark:text-white/60"
            data-testid={`${prefix}-nueva`}
          >
            Empezar de cero
          </button>
        </Section>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col gap-4">
        <Section
          title="Qué quiere ver"
          testId={`${prefix}-filtros-seccion`}
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
                    fechas: { ...prev.fechas, campo: e.target.value },
                  }))
                }
                className={FIELD_CLS}
                data-testid={`${prefix}-campo-fecha`}
              >
                {source.dateFields.map((campo) => (
                  <option key={campo.value} value={campo.value}>
                    {campo.label}
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
                    fechas: { ...prev.fechas, preset: e.target.value as QueryRangePreset },
                  }))
                }
                className={FIELD_CLS}
                data-testid={`${prefix}-preset`}
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
            testIdPrefix={prefix}
          />
        </Section>

        {error && <ErrorNotice message={error} />}
        {result && (
          <CoverageNotice
            cobertura={result.cobertura}
            fields={fields}
            ambito={ambito}
            testIdPrefix={prefix}
          />
        )}

        <Section title="Resultado" testId={`${prefix}-resultado`}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="text-2xl font-semibold tabular-nums" data-testid={`${prefix}-total`}>
                {busy && !result ? "…" : formatInt(result?.total ?? 0)}{" "}
                <span className="text-sm font-normal text-[#6B7280] dark:text-white/50">
                  {result?.total === 1 ? singular : pluralNoun}
                </span>
              </p>
              {result && <Comparacion result={result} testIdPrefix={prefix} />}
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <ColumnPicker
                visible={visibleColumns}
                onChange={applyColumns}
                columns={allColumns}
                testId={`${prefix}-column-picker`}
              />
              <button
                type="button"
                onClick={() => void handleCopyLink()}
                className="rounded-lg border border-[#DFE5ED] px-3 py-1.5 text-xs font-semibold text-[#6B7280] hover:text-[#557EFF] dark:border-white/15 dark:text-white/60"
                data-testid={`${prefix}-enlace`}
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
                data-testid={`${prefix}-export-xlsx`}
              >
                {exporting ? "Exportando…" : "Exportar a Excel"}
              </button>
              {CSV_EXPORT_VISIBLE && (
                <button
                  type="button"
                  onClick={() => void handleExport("csv")}
                  disabled={exporting || !result || result.total === 0}
                  className="rounded-lg border border-[#DFE5ED] px-3 py-1.5 text-xs font-semibold text-[#6B7280] disabled:opacity-40 dark:border-white/15 dark:text-white/60"
                  data-testid={`${prefix}-export-csv`}
                >
                  CSV
                </button>
              )}
            </div>
          </div>

          {nombrando !== null && (
            <form
              className="flex flex-wrap items-center gap-2 rounded-xl bg-[#F5F7FA] p-2 dark:bg-white/5"
              onSubmit={(e) => {
                e.preventDefault();
                void handleSave(nombrando);
              }}
              data-testid={`${prefix}-nombrar`}
            >
              <label className="text-xs font-semibold" htmlFor={`${prefix}-nombre`}>
                Nombre
              </label>
              <input
                id={`${prefix}-nombre`}
                autoFocus
                value={nombrando}
                onChange={(e) => setNombrando(e.target.value)}
                placeholder="Traspasos con prenda de agosto"
                className={`${FIELD_CLS} min-w-[16rem] flex-1`}
                data-testid={`${prefix}-nombre-input`}
              />
              <button
                type="submit"
                disabled={!nombrando.trim()}
                className="rounded-lg bg-[#557EFF] px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-40"
                data-testid={`${prefix}-guardar-confirmar`}
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
            {presets.map((preset) => (
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
                : `Ningún ${singular} cumple estos filtros. Quite alguna ficha o amplíe el periodo.`}
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
                        key={rowKey(fila)}
                        className="border-b border-[#EEF1F5] last:border-0 dark:border-white/5"
                      >
                        {columns.map((column) => (
                          <td
                            key={column.id}
                            className={`px-2 py-1.5 ${column.numeric ? "text-right tabular-nums" : ""}`}
                          >
                            {renderCell?.(column.id, fila) ?? column.value(fila)}
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
                      data-testid={`${prefix}-siguiente`}
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
function Comparacion<TRow>({
  result,
  testIdPrefix,
}: {
  result: QueryResult<TRow>;
  testIdPrefix: string;
}) {
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
    <p
      className="text-[11px] text-[#6B7280] dark:text-white/50"
      data-testid={`${testIdPrefix}-comparacion`}
    >
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
