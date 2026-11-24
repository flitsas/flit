"use client";

// La consola de consultas de Comparendos (Reportes 2.0, pestaña Consultas).
//
// COPIA ADAPTADA de la consola de Trámites (`frontend/components/consultas/QueryConsole.tsx`), no un paquete
// compartido: mismo ritmo de peticiones, enlace vivo `?q=`, aviso de cobertura y export que recorre todas las páginas,
// con los átomos de `@flit/ui` y sin lo propio de Trámites (tipo de trámite, columnas por actor).
//
// Se abre con la tabla YA poblada y los filtros como fichas encima: se empieza por un resultado, se va acotando viendo
// bajar el contador, y «guardar» solo congela el estado al que se llegó. El catálogo de campos lo sirve el servidor.

import { memo, useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { InlineAlert, Modal, Pagination } from "@flit/ui";
import { Cargador } from "@flit/ui/Loader";
import { TABLA_HEADER_BG, TABLA_HEADER_CELL_CLS, TABLA_HEADER_FG, TABLA_ROW_HOVER_CLS, TABLA_TARJETA_SUPERFICIE_CLS } from "@flit/ui/table-styles";
import { usePaginacion } from "@flit/ui/usePaginacion";
import { formatoFecha } from "@/lib/format/date";
import {
  codigoDeError,
  esDemasiadoAmplia,
  mensajeDeError,
  QUERY_MAX_PAGE_SIZE,
  QUERY_PAGE_SIZE,
  QUERY_PAGE_SIZES,
  RANGE_PRESETS,
  type QueryCondition,
  type QueryDefinition,
  type QueryField,
  type QueryRangePreset,
  type QueryResult,
  type QuerySource,
  type SavedQuery,
} from "@/lib/api/consultas-personalizadas";
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
import { download, EXPORT_BATCH_SIZE, exportarPorLotes, queryFileName } from "./export";
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


/**
 * La huella de la PREGUNTA: fechas y condiciones, no columnas ni orden.
 *
 * Es lo que decide si lo de pantalla se apartó de la consulta guardada. Incluir las columnas
 * marcaría «modificada» por enseñar una columna más, que no cambia ni una fila del resultado.
 *
 * <p>Se compara campo a campo y en ARRAYS, no serializando los objetos tal cual. Los dos lados de
 * la comparación no vienen del mismo sitio: el de pantalla se arma aquí y omite `from`/`to` cuando
 * hay preajuste, mientras que el del servidor los devuelve explícitos en `null`. `JSON.stringify`
 * los veía distintos —y también se habría tragado un cambio de orden de claves— y marcaba
 * «modificada» nada más guardar, con la consulta recién guardada y sin tocar nada.</p>
 */
/** El preset sigue si vale para el campo de fecha; si es de los que miran adelante y el campo no, pasa a 30 días atrás. */
export function presetValido(preset: QueryRangePreset, campo: string, camposFuturos: string[] = []): QueryRangePreset {
  const futuro = RANGE_PRESETS.find((p) => p.value === preset)?.futuro;
  return futuro && !camposFuturos.includes(campo) ? "ultimos_30" : preset;
}

function huellaPregunta(definition: QueryDefinition): string {
  return JSON.stringify([
    [
      definition.fechas.campo,
      definition.fechas.preset,
      definition.fechas.from ?? null,
      definition.fechas.to ?? null,
    ],
    definition.condiciones.map((c) => [c.fieldId, c.operator, c.values]),
  ]);
}

/** Celda pintada distinto de su texto (p. ej. una insignia). Recibe el texto ya rotulado; `null` la deja como texto. */
export type RenderCell<TRow> = (columnId: string, row: TRow, texto: string) => ReactNode | null;

export interface QueryConsoleProps<TRow> {
  source: QuerySource<TRow>;
  /**
   * Las columnas, o cómo armarlas con el catálogo de `/fields` (las etiquetas de los estados salen de ahí). Si es una
   * función, debe tener identidad estable (definida fuera del componente o con `useCallback`).
   */
  columns: DataColumn<TRow>[] | ((fields: QueryField[]) => DataColumn<TRow>[]);
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
  renderCell?: RenderCell<TRow>;
  /** `data-testid` de la raíz, si el módulo ya tenía uno con el que se le conoce. */
  rootTestId?: string;
  /**
   * Columnas que siempre van, aunque un preset o una consulta guardada no las nombren (la columna Empresa del SuperAdmin
   * con «todas»: sin ella, no se sabe de quién es cada fila).
   */
  columnasFijas?: string[];
}

export function QueryConsole<TRow>({
  source,
  columns: columnasProp,
  presets,
  defaultColumns,
  rowKey,
  sheetName,
  ambito,
  renderCell,
  rootTestId,
  columnasFijas,
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
  const allColumns = useMemo(
    () => (typeof columnasProp === "function" ? columnasProp(fields) : columnasProp),
    [columnasProp, fields],
  );
  const [saved, setSaved] = useState<SavedQuery[]>([]);
  const [definition, setDefinition] = useState<QueryDefinition>(emptyDefinition);
  const [visibleColumns, setVisibleColumns] = useState<string[]>(defaultColumns);

  const [result, setResult] = useState<QueryResult<TRow> | null>(null);
  // Bug #13055 — paginación de servidor estándar, con «Filas por página» (reemplaza Anterior/Siguiente).
  // Hasta 200 filas por página (el máximo del servidor), con 50 al abrir.
  const { page, pageSize, setPage, setPageSize } = usePaginacion(QUERY_PAGE_SIZE);
  // Arranca en «consultando»: la primera petición sale tras el debounce, y mientras tanto no se dice «ningún comparendo».
  const [busy, setBusy] = useState(true);
  // El export tiene su propio estado: compartirlo con el contador dejaría el botón inservible cada
  // vez que se refresca la cifra, que es todo el rato mientras alguien ajusta los filtros.
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // «Demasiado amplia» (422) no es una avería: es el servidor diciendo cómo acotar. Va como aviso, con su texto.
  const [amplia, setAmplia] = useState<string | null>(null);
  // El error de guardar se muestra DENTRO del modal: fuera quedaría tapado por el propio modal.
  const [errorGuardar, setErrorGuardar] = useState<string | null>(null);
  const [errorNombre, setErrorNombre] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [activeId, setActiveId] = useState<string | null>(null);
  const [activeSnapshot, setActiveSnapshot] = useState<string | null>(null);
  // Nombrar y confirmar se hacen dentro de la página. `window.prompt` y `window.confirm` son
  // rápidos de escribir y peores en todo lo demás: bloquean la pestaña entera, no se pueden
  // estilar, y en algunos navegadores un usuario que marca «no volver a mostrar» los deja mudos
  // para siempre — el botón dejaría de funcionar sin decir nada.
  const [nombrando, setNombrando] = useState<string | null>(null);
  // La consulta activa ya tiene nombre: guardar puede sobrescribirla o dejarla intacta y crear una
  // aparte. Sin distinguir los dos casos, la única señal de cuál iba a pasar era escribir el mismo
  // nombre de siempre o uno distinto — nada en pantalla lo explicaba.
  const [guardarComoNueva, setGuardarComoNueva] = useState(false);
  const [porBorrar, setPorBorrar] = useState<SavedQuery | null>(null);
  const [confirmandoLimpiar, setConfirmandoLimpiar] = useState(false);

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

  // Las fijas se anteponen en el orden canónico de la definición.
  const conFijas = useCallback(
    (ids: string[]) => {
      const faltan = (columnasFijas ?? []).filter((id) => !ids.includes(id));
      return faltan.length === 0 ? ids : [...faltan, ...ids];
    },
    [columnasFijas],
  );

  const applyColumns = useCallback(
    (ids: string[]) => {
      ids = conFijas(ids);
      setVisibleColumns(ids);
      try {
        window.localStorage.setItem(source.columnsStorageKey, JSON.stringify(ids));
      } catch {
        /* la preferencia no se persiste, pero la sesión actual sí funciona */
      }
    },
    [source.columnsStorageKey, conFijas],
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

  const preguntaKey = huellaPregunta(definition);

  // Una sola petición viva: la del debounce y la de cambiar de página se cancelan entre sí (HANNAH): sin esto, una
  // página vieja que vuelve tarde pisaba el resultado de un filtro nuevo.
  const enVuelo = useRef<AbortController | null>(null);

  const load = useCallback(
    async (targetPage = 1) => {
      enVuelo.current?.abort();
      const controller = new AbortController();
      enVuelo.current = controller;
      const signal = controller.signal;
      setBusy(true);
      try {
        const data = await source.run(definition, {
          page: targetPage,
          pageSize,
          signal,
        });
        if (signal.aborted) return;
        // Los avisos se limpian al llegar la respuesta, no al pedir: así no parpadean en cada ajuste (HANNAH).
        setError(null);
        setAmplia(null);
        // El servidor solo calcula el periodo anterior en la página 1: al paginar se conserva el que ya se mostró.
        setResult((prev) =>
          targetPage > 1 && data.totalPeriodoAnterior === null && prev
            ? { ...data, totalPeriodoAnterior: prev.totalPeriodoAnterior }
            : data,
        );
      } catch (e: unknown) {
        if (signal.aborted) return;
        if (esDemasiadoAmplia(e)) {
          setError(null);
          // Las cifras de la consulta anterior ya no responden a lo que hay en pantalla.
          setResult(null);
          setAmplia(mensajeDeError(e, "La consulta trae demasiados comparendos. Acótala con un NIT o un rango de fecha más corto."));
        } else {
          setAmplia(null);
          setError(mensajeDeError(e, "No se pudo ejecutar la consulta."));
        }
      } finally {
        if (!signal.aborted) setBusy(false);
      }
    },
    // La clave serializada evita relanzar por una identidad de objeto nueva con el mismo contenido.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [definitionKey, source, pageSize],
  );

  // Un respiro antes de consultar: el contador se actualiza mientras se marcan casillas, y sin esto
  // cada clic dispararía su propia petición y ganaría la que volviera última, no la última hecha.
  // El temporizador del respiro, a la vista de `changePage`: sin esto, cambiar un filtro y pasar de página en menos de
  // 250 ms dejaba el paginador en la página pedida y la tabla en la 1, cuando el temporizador vencía después (REX).
  const respiro = useRef<number | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      respiro.current = null;
      void load(1);
    }, 250);
    respiro.current = timer;
    // Cambiar los filtros invalida la página: quedarse en la 4 de un resultado que ahora tiene 2 dejaría la tabla vacía sin motivo.
    setPage(1);
    return () => {
      window.clearTimeout(timer);
      enVuelo.current?.abort();
    };
  }, [load, setPage]);

  const changePage = useCallback(
    (next: number) => {
      if (respiro.current !== null) {
        // Había un filtro nuevo esperando: la página pedida es la de ese resultado nuevo, no la del viejo.
        window.clearTimeout(respiro.current);
        respiro.current = null;
      }
      setPage(next);
      void load(next);
    },
    [load, setPage],
  );

  const setCondiciones = useCallback((condiciones: QueryCondition[]) => {
    setDefinition((prev) => ({ ...prev, condiciones }));
  }, []);

  const openSaved = useCallback(
    (query: SavedQuery) => {
      setDefinition(query.definition);
      setActiveId(query.id);
      setActiveSnapshot(huellaPregunta(query.definition));
      setNotice(null);
      if (query.definition.columnas.length > 0) {
        setVisibleColumns(
          conFijas(query.definition.columnas.filter((id) => allColumns.some((c) => c.id === id))),
        );
      }
    },
    [allColumns, conFijas],
  );

  // Nombre de la consulta activa, si es una guardada de verdad: de fábrica no cuenta —guardar sobre
  // una la duplica, porque no vive en la base y tiene que seguir ahí tal cual.
  const sugerido = saved.find((q) => q.id === activeId && !q.deFabrica)?.nombre ?? "";

  const handleSave = useCallback(
    async (nombre: string, idParaSobrescribir?: string) => {
      if (!nombre.trim()) return;
      setErrorGuardar(null);
      setErrorNombre(null);
      try {
        const guardada = await source.save({
          id: idParaSobrescribir,
          nombre: nombre.trim(),
          definition: { ...definition, columnas: visibleColumns },
        });

        setSaved((prev) => [guardada, ...prev.filter((q) => q.id !== guardada.id)]);
        setActiveId(guardada.id);
        setActiveSnapshot(huellaPregunta(guardada.definition));
        setNombrando(null);
        setNotice(`Consulta «${guardada.nombre}» guardada.`);
      } catch (e: unknown) {
        // Nombre repetido: se dice junto al campo del nombre, que es lo que hay que cambiar.
        if (codigoDeError(e) === "NOMBRE_REPETIDO") setErrorNombre(mensajeDeError(e, "Ya tienes una consulta con ese nombre."));
        else setErrorGuardar(mensajeDeError(e, "No se pudo guardar la consulta."));
      }
    },
    [definition, source, visibleColumns],
  );

  // Sobrescribir no pide nombre: ya lo tiene, y pedirlo de nuevo solo sería una oportunidad de
  // escribirlo distinto por accidente y duplicar en vez de actualizar.
  const handleOverwrite = useCallback(() => {
    if (activeId) void handleSave(sugerido, activeId);
  }, [activeId, handleSave, sugerido]);

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
        setPorBorrar(null);
        setError(mensajeDeError(e, "No se pudo borrar la consulta."));
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
   *
   * <p>Se reparte en varios archivos de {@link EXPORT_BATCH_SIZE} filas en vez de truncar: el motor
   * de consultas ya impide que `result.total` sea inmanejable (`MaxUniverso`), así que el único
   * límite que queda por resolver aquí es el tamaño cómodo de UN archivo de Excel, no cuánto puede
   * ver el usuario. Cada archivo se descarga apenas se arma, sin esperar a que el usuario pida «el resto».
   * Ojo: tras la primera descarga el navegador puede pedir permiso para «descargar varios archivos».</p>
   */
  // El recorrido del export se cancela si la consola se desmonta (cambiar de pestaña o de empresa a mitad de camino).
  const exportando = useRef<AbortController | null>(null);
  useEffect(() => () => exportando.current?.abort(), []);

  const handleExport = useCallback(
    async (formato: "xlsx" | "csv") => {
      if (!result || result.total === 0) return;

      setExporting(true);
      setNotice(null);
      exportando.current?.abort();
      exportando.current = new AbortController();
      try {
        const notas = coverageLines(result.cobertura, fields);
        const nombre = saved.find((q) => q.id === activeId)?.nombre ?? null;

        const { exportadas, archivos: totalArchivos } = await exportarPorLotes<TRow>({
          total: result.total,
          pageSize: QUERY_MAX_PAGE_SIZE,
          traerPagina: async (page, pageSize) => {
            const signal = exportando.current?.signal;
            const filas = (await source.run(definition, { page, pageSize, signal })).filas;
            // Desmontada a mitad del recorrido: se corta sin descargar nada más.
            if (signal?.aborted) throw new DOMException("Exportación cancelada", "AbortError");
            return filas;
          },
          volcar: (lote, parte) => {
            const fileName = queryFileName(
              source.exportPrefix,
              nombre,
              result.desde,
              result.hasta,
              formato,
              parte,
            );

            if (formato === "xlsx") {
              download(
                buildWorkbook(sheetName, allColumns, lote, visibleColumns, notas),
                fileName,
                XLSX_MIME,
              );
            } else {
              const csv = buildCsv(allColumns, lote, visibleColumns);
              const conNotas =
                notas.length > 0
                  ? `${csv}\r\n\r\n${notas.map((n) => `"${n.replace(/"/g, '""')}"`).join("\r\n")}`
                  : csv;
              download(conNotas, fileName, "text/csv;charset=utf-8;");
            }
          },
        });

        setNotice(
          totalArchivos > 1
            ? `Se exportaron ${plural(exportadas, singular, pluralNoun)} en ${plural(
                totalArchivos,
                "archivo",
                "archivos",
              )} de hasta ${formatInt(EXPORT_BATCH_SIZE)} filas cada uno, con ${plural(
                visibleColumns.length,
                "columna visible",
                "columnas visibles",
              )}.`
            : `Se exportaron ${plural(exportadas, singular, pluralNoun)} con ${plural(
                visibleColumns.length,
                "columna visible",
                "columnas visibles",
              )}.`,
        );
      } catch (e: unknown) {
        if (exportando.current?.signal.aborted) return;
        if (esDemasiadoAmplia(e)) setAmplia(mensajeDeError(e, "La consulta trae demasiados comparendos."));
        else setError(`No se pudo exportar: ${mensajeDeError(e, "intenta de nuevo")}`);
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

  // Memo: con 200 filas y muchas columnas, recalcular la lista en cada tecla del nombre del modal no sale gratis.
  const columns = useMemo(() => allColumns.filter((c) => visibleColumns.includes(c.id)), [allColumns, visibleColumns]);
  const presetActivo = activePreset(presets, visibleColumns);
  const modificada = activeSnapshot !== null && activeSnapshot !== preguntaKey;

  // Si «empezar de cero» tirara esto sin avisar, armar una consulta larga y darle sin querer
  // perdería el trabajo entero. Solo hace falta confirmar cuando hay algo que de verdad se pierde:
  // una consulta en blanco no tiene nada que tirar, y una guardada tal cual sigue estando guardada.
  const definicionVacia =
    definition.condiciones.length === 0 && definition.fechas.preset === "ultimos_30";
  const hayTrabajoSinGuardar = !definicionVacia && (activeId === null || modificada);

  function limpiar() {
    setDefinition(emptyDefinition());
    setActiveId(null);
    setActiveSnapshot(null);
    setConfirmandoLimpiar(false);
  }

  return (
    <div className="flex flex-col gap-6 lg:flex-row" data-testid={rootTestId ?? `${prefix}-tab`}>
      <div className="flex min-w-0 flex-1 flex-col gap-4">
        <Section
          title="Qué quieres ver"
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
                    // Un «Próximos …» sobre una fecha que ya pasó no tiene sentido: vuelve al periodo por defecto.
                    fechas: {
                      ...prev.fechas,
                      campo: e.target.value,
                      preset: presetValido(prev.fechas.preset, e.target.value, source.camposFuturos),
                    },
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
                {RANGE_PRESETS.filter((p) => !p.futuro || (source.camposFuturos ?? []).includes(definition.fechas.campo)).map((preset) => (
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
                  max={definition.fechas.to ?? undefined}
                  onChange={(from) =>
                    setDefinition((prev) => ({ ...prev, fechas: { ...prev.fechas, from } }))
                  }
                />
                <DateField
                  label="Hasta"
                  value={definition.fechas.to ?? ""}
                  min={definition.fechas.from ?? undefined}
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

        {amplia && (
          <div data-testid={`${prefix}-demasiado-amplia`}>
            <InlineAlert tone="warning" title="Acota la consulta">
              {amplia}
            </InlineAlert>
          </div>
        )}
        {error && <ErrorNotice message={error} />}
        <Section title="Resultado" testId={`${prefix}-resultado`}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="text-2xl font-semibold tabular-nums" data-testid={`${prefix}-total`}>
                {busy && !result ? "…" : amplia ? "\u2014" : formatInt(result?.total ?? 0)}{" "}
                <span className="text-sm font-normal text-muted-foreground">
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
                className="rounded-lg border border-border px-3 py-1.5 text-xs font-semibold text-muted-foreground hover:text-flit-brand"
                data-testid={`${prefix}-enlace`}
              >
                Copiar enlace
              </button>
              <PrimaryButton
                onClick={() => {
                  setGuardarComoNueva(false);
                  setErrorGuardar(null);
                  setErrorNombre(null);
                  setNombrando("");
                }}
                disabled={exporting}
              >
                Guardar consulta
              </PrimaryButton>
              <button
                type="button"
                onClick={() => void handleExport("xlsx")}
                // Con una consulta en curso el resultado a la vista ya no es el de los filtros: exportar mezclaría las dos.
                disabled={exporting || busy || !result || result.total === 0}
                className="rounded-lg border border-[color:var(--di-border-control)] bg-[color:var(--di-surface-elevated)] px-3 py-1.5 text-xs font-semibold text-[color:var(--di-action)] transition hover:bg-[color:var(--di-accent-soft)] disabled:opacity-40"
                data-testid={`${prefix}-export-xlsx`}
              >
                {exporting ? "Exportando…" : "Exportar a Excel"}
              </button>
              {CSV_EXPORT_VISIBLE && (
                <button
                  type="button"
                  onClick={() => void handleExport("csv")}
                  disabled={exporting || busy || !result || result.total === 0}
                  className="rounded-lg border border-border px-3 py-1.5 text-xs font-semibold text-muted-foreground disabled:opacity-40"
                  data-testid={`${prefix}-export-csv`}
                >
                  CSV
                </button>
              )}
            </div>
          </div>

          {/* La cobertura va junto al contador, dentro de «Resultado»: explica el número que se acaba de leer, y al
              aparecer o desaparecer no empuja la sección entera (HANNAH). */}
          {result && <CoverageNotice cobertura={result.cobertura} fields={fields} ambito={ambito} testIdPrefix={prefix} />}

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
                    ? "border-flit-brand bg-flit-brand/10 text-flit-brand-ink"
                    : "border-border text-muted-foreground hover:text-flit-brand"
                }`}
              >
                {preset.label}
              </button>
            ))}
          </div>

          {notice && (
            <p className="text-[11px] text-muted-foreground" role="status">
              {notice}
            </p>
          )}

          {/* Primera carga: el cargador del kit DENTRO del bloque, con su altura reservada (sin modal ni salto). Los
              refrescos posteriores (filtros en vivo) dejan la tabla anterior a la vista. */}
          {busy && !result ? (
            <div className="flex min-h-64 items-center justify-center" role="status" aria-label="Consultando">
              <Cargador />
            </div>
          ) : !result || result.filas.length === 0 ? (
            <div className="flex min-h-64 items-center justify-center text-center">
              <Empty>
              {busy
                ? "Consultando…"
                : amplia
                  ? "Acota los filtros para ver el resultado."
                  : `Ningún ${singular} cumple estos filtros. Quita alguna ficha o amplía el periodo.`}
              </Empty>
            </div>
          ) : (
            <>
              {/* La tabla ancha se desplaza dentro de su caja; la página nunca. */}
              <div className={`${TABLA_TARJETA_SUPERFICIE_CLS} overflow-x-auto overscroll-x-contain`} data-testid={`${prefix}-tabla`}>
                <table className="w-full min-w-[48rem] text-xs" aria-label={`Resultado de la consulta de ${pluralNoun}`} aria-busy={busy}>
                  <thead>
                    <tr>
                      {columns.map((column) => (
                        <th
                          key={column.id}
                          scope="col"
                          className={`${TABLA_HEADER_CELL_CLS} whitespace-nowrap ${column.numeric ? "text-right" : ""}`}
                          style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                        >
                          {column.label}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {result.filas.map((fila) => (
                      <Fila key={rowKey(fila)} fila={fila} columns={columns} renderCell={renderCell} />
                    ))}
                  </tbody>
                </table>
              </div>

              <Pagination
                page={page}
                pageSize={pageSize}
                totalCount={result.total}
                onPageChange={changePage}
                onPageSizeChange={setPageSize}
                pageSizeOptions={QUERY_PAGE_SIZES}
                noun={pluralNoun}
                ariaLabel="Paginación de la consulta"
              />
            </>
          )}
        </Section>
      </div>

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
            onSchedule={source.onSchedule}
            testIdPrefix={prefix}
          />
          <button
            type="button"
            onClick={() => (hayTrabajoSinGuardar ? setConfirmandoLimpiar(true) : limpiar())}
            className="mt-1 w-full rounded-xl border border-flit-brand/30 bg-flit-brand/[0.06] px-2 py-2 text-[11px] font-semibold text-flit-brand transition hover:border-flit-brand hover:bg-flit-brand/10 dark:border-flit-brand/30 dark:bg-flit-brand/10 dark:text-[color:var(--di-action)]"
            data-testid={`${prefix}-nueva`}
          >
            Empezar de cero
          </button>
        </Section>
      </aside>

      <Modal
        open={confirmandoLimpiar}
        onClose={() => setConfirmandoLimpiar(false)}
        title="¿Empezar de cero?"
        description="Se perderán los filtros que no has guardado."
        size="sm"
      >
        <div className="flex justify-end gap-2" data-testid={`${prefix}-limpiar-confirmar`}>
          <button
            type="button"
            onClick={() => setConfirmandoLimpiar(false)}
            className="rounded-lg border border-border bg-card px-3 py-2 text-xs font-semibold text-muted-foreground hover:text-foreground"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={limpiar}
            className="rounded-lg bg-destructive px-3 py-2 text-xs font-semibold text-destructive-foreground hover:bg-destructive/90"
            data-testid={`${prefix}-limpiar-confirmar-si`}
          >
            Empezar de cero
          </button>
        </div>
      </Modal>

      <Modal open={nombrando !== null} onClose={() => setNombrando(null)} title="Guardar consulta" size="sm">
        {errorGuardar && (
          <div className="mb-3" data-testid={`${prefix}-guardar-error`}>
            <InlineAlert tone="error" compact>
              {errorGuardar}
            </InlineAlert>
          </div>
        )}
        {sugerido && !guardarComoNueva ? (
          // Hay una consulta guardada detrás de esto: lo primero que se pregunta no es «cómo se
          // llama», es «actualizo la de siempre o dejo esta aparte». Sin esta pantalla, la única
          // forma de saber cuál iba a pasar era fijarse en si el nombre que se escribía coincidía
          // con el de siempre — nada lo explicaba.
          <div className="flex flex-col gap-4" data-testid={`${prefix}-guardar-elegir`}>
            <p className="text-xs leading-relaxed text-muted-foreground">
              Esta consulta viene de{" "}
              <span className="font-semibold text-foreground dark:text-muted-foreground">«{sugerido}»</span>.
              Puedes actualizarla con los cambios de ahora, o guardarlos aparte sin tocar la
              original.
            </p>
            <div className="flex flex-col gap-2 sm:flex-row sm:justify-end">
              <button
                type="button"
                onClick={() => {
                  setGuardarComoNueva(true);
                  setNombrando("");
                }}
                className="rounded-lg border border-border bg-card px-3 py-2 text-xs font-semibold text-muted-foreground hover:text-foreground"
                data-testid={`${prefix}-guardar-como-nueva`}
              >
                Guardar como nueva
              </button>
              <button
                type="button"
                onClick={handleOverwrite}
                className="rounded-lg bg-flit-brand px-3 py-2 text-xs font-semibold text-white"
                data-testid={`${prefix}-guardar-sobrescribir`}
              >
                Sobrescribir «{sugerido}»
              </button>
            </div>
          </div>
        ) : (
          <form
            className="flex flex-col gap-3"
            onSubmit={(e) => {
              e.preventDefault();
              if (nombrando) void handleSave(nombrando, undefined);
            }}
            data-testid={`${prefix}-nombrar`}
          >
            <label className="flex flex-col gap-1 text-xs font-semibold" htmlFor={`${prefix}-nombre`}>
              Nombre
              <input
                id={`${prefix}-nombre`}
                autoFocus
                value={nombrando ?? ""}
                onChange={(e) => {
                  setNombrando(e.target.value);
                  setErrorNombre(null);
                }}
                placeholder="Fotomultas de Bogotá sin gestión"
                className={FIELD_CLS}
                aria-invalid={errorNombre ? true : undefined}
                aria-describedby={errorNombre ? `${prefix}-nombre-error` : undefined}
                data-testid={`${prefix}-nombre-input`}
              />
            </label>
            {errorNombre && (
              <InlineAlert tone="error" compact id={`${prefix}-nombre-error`}>
                {errorNombre}
              </InlineAlert>
            )}
            <div className="flex items-center justify-between gap-2">
              {sugerido ? (
                <button
                  type="button"
                  onClick={() => setGuardarComoNueva(false)}
                  className="text-[11px] font-semibold text-muted-foreground hover:text-foreground dark:hover:text-foreground"
                >
                  ← Volver
                </button>
              ) : (
                <span />
              )}
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => setNombrando(null)}
                  className="rounded-lg border border-border bg-card px-3 py-2 text-xs font-semibold text-muted-foreground hover:text-foreground"
                >
                  Cancelar
                </button>
                <button
                  type="submit"
                  disabled={!nombrando?.trim()}
                  className="rounded-lg bg-flit-brand px-3 py-2 text-xs font-semibold text-white disabled:opacity-40"
                  data-testid={`${prefix}-guardar-confirmar`}
                >
                  Guardar
                </button>
              </div>
            </div>
          </form>
        )}
      </Modal>
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
  // `null`: el servidor no lo calculó (no cabe en el tope). No se compara contra nada; solo se dice el rango.
  if (anterior === null) {
    return (
      <p className="text-[11px] text-muted-foreground">
        {formatoFecha(result.desde)} a {formatoFecha(result.hasta)}
      </p>
    );
  }
  if (anterior === 0) {
    return (
      <p className="text-[11px] text-muted-foreground">
        {formatoFecha(result.desde)} a {formatoFecha(result.hasta)} · nada comparable en el periodo previo
      </p>
    );
  }

  const delta = ((result.total - anterior) / anterior) * 100;
  const signo = delta >= 0 ? "+" : "";

  return (
    <p
      className="text-[11px] text-muted-foreground"
      data-testid={`${testIdPrefix}-comparacion`}
    >
      {formatoFecha(result.desde)} a {formatoFecha(result.hasta)} · {signo}
      {delta.toFixed(0)} % frente a los {formatInt(anterior)} del periodo anterior
    </p>
  );
}

function DateField({
  label,
  value,
  onChange,
  min,
  max,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  /** Límites cruzados: Desde no pasa de Hasta y Hasta no baja de Desde. */
  min?: string;
  max?: string;
}) {
  return (
    <label className="flex flex-col gap-1 text-xs font-semibold">
      {label}
      <input
        type="date"
        value={value}
        min={min || undefined}
        max={max || undefined}
        onChange={(e) => onChange(e.target.value)}
        className={FIELD_CLS}
      />
    </label>
  );
}

/**
 * Una fila de la tabla, memorizada: al escribir el nombre en el modal, o al abrir una ficha, la consola se vuelve a
 * pintar, y repintar 200 filas × N columnas por cada tecla se nota. Solo cambia si cambia la fila o las columnas.
 */
const Fila = memo(function Fila<TRow>({
  fila,
  columns,
  renderCell,
}: {
  fila: TRow;
  columns: DataColumn<TRow>[];
  renderCell?: RenderCell<TRow>;
}) {
  return (
    <tr className={`border-b border-[color:var(--table-row-border)] bg-card ${TABLA_ROW_HOVER_CLS}`}>
      {columns.map((column) => (
        <td key={column.id} className={`whitespace-nowrap px-3.5 py-2.5 align-middle ${column.numeric ? "text-right tabular-nums" : ""}`}>
          {renderCell?.(column.id, fila, column.value(fila)) ?? column.value(fila)}
        </td>
      ))}
    </tr>
  );
}) as <TRow>(props: { fila: TRow; columns: DataColumn<TRow>[]; renderCell?: RenderCell<TRow> }) => ReactNode;
