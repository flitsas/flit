"use client";

// Trazabilidad ICT (Feature #11814). Módulo NUEVO: no sustituye a «Log ICT», que se queda como la
// capa técnica de bajo nivel, ni a «Reportes ICT», que sigue siendo la vista agregada por compañía.
// Lo que aporta es el nivel que faltaba: el trámite.
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ChevronRight, FileSpreadsheet, Search, X } from "lucide-react";
import { ModuleTitle } from "./ModuleTitle";
import { PageNav } from "@/components/atom/PageNav";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { WIZARD_CTA_GRADIENT } from "@/components/operacion/wizard-field-styles";
import {
  fetchTramitesIct,
  type FiltrosTramitesIct,
  type PaginaTramitesIct,
  type TramiteIct,
} from "@/lib/api/ict-trazabilidad";
import {
  ESTADOS_ICT,
  ESTADO_ICT,
  MINUTOS_ESPERA_ALTA,
  estadoIctLabel,
  formatearEspera,
  type EstadoIct,
} from "@/lib/ict/trazabilidad";
import { bogotaClock, buildXlsx, XLSX_MIME, type XlsxCell } from "@/lib/xlsx";
import { DetalleTramiteIct } from "@/components/ict/DetalleTramiteIct";
import { decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";
import { getToken } from "@/lib/api/client";

const TAMANO_PAGINA = 25;

/** Cabeceras de la tabla; la última («Esperando») se pinta aparte por el redondeo. */
const COLUMNAS = [
  "N.º",
  "Placa",
  "Trámite",
  "Compañía",
  "Radicador",
  "Estado",
  "Señales",
] as const;

const tdCls = "border-y px-4 py-3 align-middle";

const inputCls =
  "rounded-lg border border-[#D9DEE8] dark:border-white/15 bg-white dark:bg-[#0B0F14] px-2.5 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20";

const ghostCls =
  "inline-flex items-center gap-1.5 rounded-lg border border-[#D9DEE8] dark:border-white/15 px-3 py-2 text-xs font-medium opacity-80 hover:opacity-100 hover:border-[#557EFF]";

interface Filtros {
  desde: string;
  hasta: string;
  placas: string;
  numero: string;
  estado: string;
}

const FILTROS_VACIOS: Filtros = { desde: "", hasta: "", placas: "", numero: "", estado: "" };

function hoyMenos(dias: number): string {
  const d = new Date();
  d.setDate(d.getDate() - dias);
  return d.toISOString().slice(0, 10);
}

function filtrosIniciales(): Filtros {
  // Ventana por defecto de 30 días: sin acotar, la bandeja arranca escaneando cientos de miles de
  // filas para enseñar las 25 primeras.
  return { ...FILTROS_VACIOS, desde: hoyMenos(30), hasta: new Date().toISOString().slice(0, 10) };
}

function aParametros(f: Filtros, page: number): FiltrosTramitesIct {
  const numero = Number.parseInt(f.numero.trim(), 10);
  return {
    desde: f.desde || undefined,
    hasta: f.hasta || undefined,
    placas: f.placas.trim() || undefined,
    // Se manda solo si es un número: el backend busca por igualdad exacta y una cadena con letras
    // no es un número de trámite, es un error de tecleo.
    numero: Number.isSafeInteger(numero) && numero > 0 ? numero : undefined,
    estado: f.estado || undefined,
    page,
    pageSize: TAMANO_PAGINA,
  };
}

export function IctTrazabilidad() {
  const [filtros, setFiltros] = useState<Filtros>(filtrosIniciales);
  const [aplicados, setAplicados] = useState<Filtros>(filtrosIniciales);
  const [page, setPage] = useState(1);
  const [pagina, setPagina] = useState<PaginaTramitesIct | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(true);
  const [abierto, setAbierto] = useState<number | null>(null);
  // El enlace al trámite solo necesita el tenant de la fila cuando quien mira es SuperAdmin; para
  // el resto lo deriva su sesión. Se resuelve tras montar: en el servidor no hay localStorage.
  const [esAdmin, setEsAdmin] = useState(false);
  useEffect(() => {
    // El JWT vive en localStorage y no existe en el servidor: leerlo durante el render rompería la
    // hidratación. Mismo patrón que LogQx.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setEsAdmin(isSuperAdmin(decodeJwtPayload(getToken())));
  }, []);

  // Evita que una respuesta lenta de una búsqueda anterior pise a la de la búsqueda actual.
  const peticion = useRef(0);

  const cargar = useCallback(async (f: Filtros, p: number) => {
    const propia = ++peticion.current;
    setCargando(true);
    try {
      const datos = await fetchTramitesIct(aParametros(f, p));
      if (peticion.current !== propia) return;
      setPagina(datos);
      setError(null);
    } catch {
      if (peticion.current !== propia) return;
      // Mensaje comprensible: nunca el código HTTP ni la ruta técnica.
      setError("No se pudieron cargar los trámites. Revisa los filtros e inténtalo de nuevo.");
    } finally {
      if (peticion.current === propia) setCargando(false);
    }
  }, []);

  useEffect(() => {
    // `cargar` marca el indicador de carga ANTES de esperar la respuesta; hacerlo después dejaría
    // la pantalla sin señal de que está trabajando durante toda la consulta, que es cuando más
    // falta hace.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void cargar(aplicados, page);
  }, [cargar, aplicados, page]);

  const aplicar = useCallback((f: Filtros) => {
    setAplicados(f);
    setPage(1);
    setAbierto(null);
  }, []);

  const limpiar = useCallback(() => {
    const iniciales = filtrosIniciales();
    setFiltros(iniciales);
    aplicar(iniciales);
  }, [aplicar]);

  const alternarEstado = useCallback(
    (estado: EstadoIct) => {
      // Volver a pulsar el contador activo retira el filtro: es la única forma de salir sin buscar
      // el botón de limpiar.
      const siguiente = { ...filtros, estado: filtros.estado === estado ? "" : estado };
      setFiltros(siguiente);
      aplicar(siguiente);
    },
    [aplicar, filtros],
  );

  const hayFiltros = useMemo(
    () => Boolean(aplicados.placas || aplicados.numero || aplicados.estado),
    [aplicados],
  );

  const items = pagina?.items ?? null;
  const total = pagina?.total ?? 0;
  const totalPaginas = Math.max(1, Math.ceil(total / TAMANO_PAGINA));

  const status = cargando && pagina === null
    ? "loading"
    : error !== null && pagina === null
      ? "error"
      : items !== null && items.length === 0
        ? "empty"
        : "ready";

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <ModuleTitle
        title="Trazabilidad ICT"
        subtitle="Qué pasó con cada trámite que entró por la integración con terceros. Busca por número, placa o VIN y abre su recorrido completo."
      />

      {/* Tira de contadores por estado, con la presentación de los KPIs de Trámites
          (`EstadoFunnel`): tarjeta única dividida en columnas, icono en pastilla del tono del
          estado, etiqueta y conteo. Los siete estados tienen color propio. */}
      <div
        role="group"
        aria-label="Contadores por estado"
        className="grid shrink-0 grid-cols-2 divide-[#EEF2F7] overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white sm:grid-cols-4 sm:divide-x lg:grid-cols-7 dark:divide-white/5 dark:border-white/10 dark:bg-[#162744]"
      >
        {ESTADOS_ICT.map((estado) => {
          const meta = ESTADO_ICT[estado];
          const Icon = meta.Icon;
          const activo = aplicados.estado === estado;
          const conteo = pagina?.conteoPorEstado?.[estado] ?? 0;
          return (
            <button
              key={estado}
              type="button"
              aria-label={`${meta.label}: ${conteo} trámite${conteo === 1 ? "" : "s"}`}
              aria-pressed={activo}
              onClick={() => alternarEstado(estado)}
              className="flex flex-col items-center gap-1 px-2 py-3 transition hover:bg-[#557EFF]/[0.06] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF]"
              style={activo ? { background: meta.style.bg } : undefined}
            >
              {/* El icono es elemento gráfico (umbral 3:1): lleva el tono PURO del estado. */}
              <span
                className="grid h-8 w-8 shrink-0 place-items-center rounded-full"
                style={{ background: meta.style.bg }}
              >
                <Icon className="h-4 w-4" style={{ color: meta.style.accent }} aria-hidden="true" />
              </span>
              <span className="max-w-full truncate text-xs font-medium text-[#162744]/70 dark:text-white/70">
                {meta.label}
              </span>
              <span
                className="text-xl font-bold leading-none tabular-nums text-[#162744] dark:text-white"
                aria-hidden="true"
              >
                {conteo}
              </span>
              {/* El filtro activo no depende solo del fondo. */}
              <span
                className="h-0.5 w-6 rounded-full"
                style={{ background: activo ? meta.style.color : "transparent" }}
                aria-hidden="true"
              />
            </button>
          );
        })}
      </div>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          aplicar(filtros);
        }}
        className="flex flex-wrap items-end gap-2 shrink-0 rounded-2xl border border-[#DFE5ED] dark:border-white/10 bg-white dark:bg-[#0B0F14] p-3"
        role="search"
        aria-label="Filtros de la trazabilidad ICT"
      >
        <Campo label="Desde" htmlFor="it-desde">
          <input
            id="it-desde"
            type="date"
            value={filtros.desde}
            onChange={(e) => setFiltros({ ...filtros, desde: e.target.value })}
            className={inputCls}
          />
        </Campo>
        <Campo label="Hasta" htmlFor="it-hasta">
          <input
            id="it-hasta"
            type="date"
            value={filtros.hasta}
            onChange={(e) => setFiltros({ ...filtros, hasta: e.target.value })}
            className={inputCls}
          />
        </Campo>
        <Campo label="N.º de trámite" htmlFor="it-numero">
          <input
            id="it-numero"
            type="text"
            inputMode="numeric"
            placeholder="10461"
            value={filtros.numero}
            onChange={(e) => setFiltros({ ...filtros, numero: e.target.value })}
            className={`${inputCls} w-[110px]`}
          />
        </Campo>
        {/* Un solo campo para placas y VIN: quien busca pega lo que le mandan sin distinguir cuál
            de los dos tiene delante. */}
        <Campo label="Placas o VIN" htmlFor="it-placas">
          <input
            id="it-placas"
            type="text"
            placeholder="NPT415, LTS304"
            value={filtros.placas}
            onChange={(e) => setFiltros({ ...filtros, placas: e.target.value })}
            className={`${inputCls} w-[210px]`}
            aria-describedby="it-placas-pista"
          />
        </Campo>
        <span id="it-placas-pista" className="sr-only">
          Puedes escribir varias separadas por coma.
        </span>
        <span className="flex-1" />
        {items && items.length > 0 && (
          <button
            type="button"
            onClick={() => exportar(items, aplicados, total)}
            className={ghostCls}
          >
            <FileSpreadsheet className="h-3.5 w-3.5" aria-hidden="true" /> Exportar a Excel
          </button>
        )}
        {(hayFiltros || cargando) && (
          <button type="button" onClick={limpiar} className={ghostCls}>
            <X className="h-3.5 w-3.5" aria-hidden="true" /> Limpiar
          </button>
        )}
        <button
          type="submit"
          disabled={cargando}
          className="flex items-center gap-2 rounded-lg px-4 py-2 text-xs font-semibold text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
          style={{ background: WIZARD_CTA_GRADIENT }}
        >
          <Search className="h-3.5 w-3.5" aria-hidden="true" /> Buscar
        </button>
      </form>

      <UiStateBoundary
        status={status}
        skeletonRows={6}
        errorMessage={error ?? "No se pudo cargar la trazabilidad ICT."}
        onRetry={() => void cargar(aplicados, page)}
        emptyMessage="Ningún trámite de la integración coincide con los filtros. Amplía el rango de fechas o quita algún filtro."
      >
        {items && items.length > 0 && (
          <>
            {/* Tabla en el patrón del resto de la consola (companies / trámites): cabecera en
                pastilla #DFE5ED y cada fila como tarjeta blanca separada. */}
            <div className="overflow-x-auto">
              <table className="w-full min-w-[1040px] border-separate border-spacing-y-2 text-xs">
                <thead>
                  <tr
                    className="text-left text-[10px] font-semibold uppercase"
                    style={{ color: "#162744" }}
                  >
                    <th className="rounded-l-xl px-3 py-2.5" style={{ background: "#DFE5ED", width: 34 }}>
                      <span className="sr-only">Detalle</span>
                    </th>
                    {COLUMNAS.map((c) => (
                      <th key={c} className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                        {c}
                      </th>
                    ))}
                    <th className="rounded-r-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                      Esperando
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((t) => (
                    <FilaTramite
                      key={t.id}
                      tramite={t}
                      esAdmin={esAdmin}
                      abierta={abierto === t.numero}
                      onToggle={() => setAbierto(abierto === t.numero ? null : t.numero)}
                    />
                  ))}
                </tbody>
              </table>
            </div>

            <PageNav
              page={page}
              totalPages={totalPaginas}
              onPageChange={setPage}
              resumen={`Mostrando ${(page - 1) * TAMANO_PAGINA + 1}–${
                Math.min(page * TAMANO_PAGINA, total)
              } de ${total.toLocaleString("es-CO")} trámites`}
              ariaLabel="Paginación de la trazabilidad ICT"
            />
          </>
        )}
      </UiStateBoundary>
    </div>
  );
}

function Campo({
  label,
  htmlFor,
  children,
}: {
  label: string;
  htmlFor: string;
  children: React.ReactNode;
}) {
  return (
    <label htmlFor={htmlFor} className="flex flex-col gap-1 text-[10px]">
      <span className="font-semibold uppercase tracking-wider opacity-55">{label}</span>
      {children}
    </label>
  );
}

function FilaTramite({
  tramite,
  esAdmin,
  abierta,
  onToggle,
}: {
  tramite: TramiteIct;
  esAdmin: boolean;
  abierta: boolean;
  onToggle: () => void;
}) {
  const meta = ESTADO_ICT[tramite.estado];
  const estilo = meta?.style;
  const alta =
    tramite.minutosEsperando !== null && tramite.minutosEsperando >= MINUTOS_ESPERA_ALTA;

  const señales: string[] = [];
  if (tramite.pausado) señales.push("Pausado");
  if (tramite.sinAdjuntos) señales.push("Sin adjuntos");

  return (
    <>
      <tr
        className={`cursor-pointer bg-white text-[#162744] transition dark:bg-[#162744] dark:text-white ${
          abierta ? "border-[#557EFF]/40" : "border-[#DFE5ED] dark:border-white/10"
        }`}
        onClick={onToggle}
        tabIndex={0}
        role="button"
        aria-expanded={abierta}
        aria-label={`Trámite ${tramite.numero}, placa ${tramite.placa}`}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            onToggle();
          }
        }}
      >
        <td className={`${tdCls} rounded-l-xl border-l px-3`}>
          <ChevronRight
            className={`h-3.5 w-3.5 text-[#557EFF] transition-transform ${abierta ? "rotate-90" : ""}`}
            aria-hidden="true"
          />
        </td>
        <td className={tdCls}>
          <span className="font-mono font-semibold text-[#557EFF]">{tramite.numero}</span>
        </td>
        <td className={tdCls}>
          <span className="font-mono font-semibold tracking-wide">{tramite.placa}</span>
          {tramite.vin && (
            <span className="block font-mono text-[10px] opacity-55">{tramite.vin}</span>
          )}
        </td>
        <td className={tdCls}>
          {tramite.tipoTramite ?? "—"}
          <span className="block text-[10px] opacity-55">{tramite.operacion ?? "—"}</span>
        </td>
        <td className={tdCls}>{tramite.compania ?? "—"}</td>
        <td className={tdCls}>{tramite.radicador || "—"}</td>
        <td className={tdCls}>
          <span
            className="inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-semibold"
            style={estilo ? { background: estilo.bg, color: estilo.color } : undefined}
          >
            <span
              className="h-1.5 w-1.5 rounded-full"
              style={{ background: estilo?.accent }}
              aria-hidden="true"
            />
            {estadoIctLabel(tramite.estado)}
          </span>
        </td>
        <td className={tdCls}>
          {señales.length === 0 ? (
            <span className="opacity-45">—</span>
          ) : (
            <span className="flex flex-col gap-0.5">
              {señales.map((s) => (
                <span key={s} className="text-[10px] font-semibold text-[#C2410C]">
                  {s}
                </span>
              ))}
            </span>
          )}
        </td>
        <td className={`${tdCls} rounded-r-xl border-r`}>
          <span
            className={`font-mono tabular-nums ${alta ? "font-semibold text-[#C2410C]" : "opacity-70"}`}
          >
            {formatearEspera(tramite.minutosEsperando)}
          </span>
        </td>
      </tr>
      {abierta && (
        <tr>
          <td colSpan={COLUMNAS.length + 2} className="px-0 pb-2 pt-0">
            <DetalleTramiteIct tramite={tramite} esAdmin={esAdmin} />
          </td>
        </tr>
      )}
    </>
  );
}

/**
 * Exportación a XLSX con el escritor propio del repositorio (`lib/xlsx`), sin dependencias externas.
 *
 * Exporta la PÁGINA a la vista, y lo dice dentro del archivo: el .xlsx es lo que se reenvía por
 * correo a quien no ejecutó la búsqueda, así que la advertencia tiene que viajar con él y no
 * quedarse en la pantalla de quien lo generó.
 */
function exportar(items: TramiteIct[], filtros: Filtros, total: number) {
  const rows: XlsxCell[][] = items.map((t) => [
    t.numero,
    t.placa,
    t.vin ?? "",
    t.tipoTramite ?? "",
    t.operacion ?? "",
    t.compania ?? "",
    t.radicador || "",
    estadoIctLabel(t.estado),
    formatearEspera(t.minutosEsperando),
    t.pausado ? "Sí" : "No",
    t.sinAdjuntos ? "Sí" : "No",
    bogotaClock(t.recibidoEn),
  ]);

  const notes: string[] = [
    `Exportado desde Trazabilidad ICT · ${items.length} de ${total} trámites que cumplen los filtros.`,
  ];
  const aplicados: string[] = [];
  if (filtros.desde || filtros.hasta) {
    aplicados.push(`fechas ${filtros.desde || "sin inicio"} a ${filtros.hasta || "sin fin"}`);
  }
  if (filtros.numero) aplicados.push(`n.º ${filtros.numero}`);
  if (filtros.placas) aplicados.push(`placas o VIN ${filtros.placas}`);
  if (filtros.estado) aplicados.push(`estado ${estadoIctLabel(filtros.estado)}`);
  if (aplicados.length > 0) notes.push(`Filtros aplicados: ${aplicados.join(" · ")}.`);
  if (total > items.length) {
    notes.push(
      "Solo se exportó la página a la vista. Para llevarte el resto, avanza de página y vuelve a exportar.",
    );
  }

  const xlsx = buildXlsx({
    name: "Trazabilidad ICT",
    columns: [
      { header: "N.º de trámite", width: 15 },
      { header: "Placa", width: 11 },
      { header: "VIN", width: 20 },
      { header: "Tipo de trámite", width: 24 },
      { header: "Operación", width: 13 },
      { header: "Compañía", width: 30 },
      { header: "Radicador", width: 28 },
      { header: "Estado", width: 20 },
      { header: "Esperando", width: 14 },
      { header: "Pausado", width: 10 },
      { header: "Sin adjuntos", width: 13 },
      { header: "Recibido", width: 20 },
    ],
    rows,
    notes,
  });

  const blob = new Blob([xlsx as BlobPart], { type: XLSX_MIME });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = "trazabilidad-ict.xlsx";
  anchor.click();
  URL.revokeObjectURL(url);
}
