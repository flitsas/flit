"use client";

import { useCallback, useEffect, useMemo, useState, useSyncExternalStore } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Download, RefreshCw } from "lucide-react";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { buildWorkbook } from "@/components/consultas/columns";
import { download, EXPORT_BATCH_SIZE, exportarPorLotes } from "@/components/consultas/export";
import { useToast } from "@/components/admin/Toast";
import {
  listRuntConfirmationAttempts,
  listRuntConfirmationRuns,
  RUNT_ATTEMPTS_MAX_PAGE_SIZE,
  RUNT_VERDICT_LABEL,
  type RuntConfirmationAttemptRow,
  type RuntConfirmationAttemptsFilter,
  type RuntConfirmationRun,
  type RuntConfirmationVerdict,
} from "@/lib/api/admin-runt-confirmation";
import { superadminClient } from "@/lib/api/superadmin-client";
import { XLSX_MIME } from "@/lib/xlsx";
import { formatFechaHora } from "@/lib/format/date";
import { HISTORIAL_EXPORT_COLUMNS, HISTORIAL_EXPORT_IDS, proveedorLabel, VERDICT_TONE, vehiculoDe } from "./historial-columns";
import { IntentoDetalle } from "./IntentoDetalle";
import { UltimaCorridaResumen } from "./UltimaCorridaResumen";

/** Mismos tamaños que /tramites; el elegido se recuerda durante la sesión (sessionStorage, con guarda). */
const TAMANOS_DE_PAGINA = [10, 25, 50, 100] as const;
const PAGE_SIZE_POR_DEFECTO = 10;
const CLAVE_PAGE_SIZE = "confirmacion-runt.historial.pageSize";

function suscripcionInerte(): () => void {
  return () => {};
}

function leerPageSizeGuardado(): number {
  try {
    const guardado = Number(sessionStorage.getItem(CLAVE_PAGE_SIZE));
    return TAMANOS_DE_PAGINA.includes(guardado as (typeof TAMANOS_DE_PAGINA)[number]) ? guardado : PAGE_SIZE_POR_DEFECTO;
  } catch {
    return PAGE_SIZE_POR_DEFECTO;
  }
}
const VERDICTS: RuntConfirmationVerdict[] = ["confirmed", "pending", "discrepancy", "unverifiable", "error"];

type Estado = "loading" | "error" | "ready";

interface FiltrosUrl {
  runId: string;
  verdict: string;
  procedureTypeCode: string;
  search: string;
  procedureInstanceId: string;
  page: number;
}

function leerFiltros(params: URLSearchParams): FiltrosUrl {
  const page = Number(params.get("page") ?? "1");
  return {
    runId: params.get("runId") ?? "",
    verdict: params.get("verdict") ?? "",
    procedureTypeCode: params.get("tipo") ?? "",
    search: params.get("q") ?? "",
    procedureInstanceId: params.get("tramite") ?? "",
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };
}

function aQuery(f: FiltrosUrl): string {
  const p = new URLSearchParams();
  if (f.runId) p.set("runId", f.runId);
  if (f.verdict) p.set("verdict", f.verdict);
  if (f.procedureTypeCode) p.set("tipo", f.procedureTypeCode);
  if (f.search) p.set("q", f.search);
  if (f.procedureInstanceId) p.set("tramite", f.procedureInstanceId);
  if (f.page > 1) p.set("page", String(f.page));
  const s = p.toString();
  return s ? `?${s}` : "";
}

const selectCls =
  "rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#162744] outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white";

/**
 * Plataforma → Confirmación RUNT → Historial (HU #12311). Una fila por intento; filtros en la URL
 * (sobreviven un refresh y se comparten por enlace); detalle expandible con motivo, regla, crudo y
 * «Consultar ahora»; export por lotes con las mismas columnas más el motivo.
 */
export function ConfirmacionRuntHistorialPanel() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const toast = useToast();

  // Se memoriza sobre el TEXTO de la query, no sobre el objeto: así un render que reciba otra
  // instancia con el mismo contenido no dispara una recarga de la tabla.
  const paramsKey = searchParams?.toString() ?? "";
  const filtros = useMemo(() => leerFiltros(new URLSearchParams(paramsKey)), [paramsKey]);
  const [busqueda, setBusqueda] = useState(filtros.search);

  const [estado, setEstado] = useState<Estado>("loading");
  const [rows, setRows] = useState<RuntConfirmationAttemptRow[]>([]);
  const [total, setTotal] = useState(0);
  const [expandido, setExpandido] = useState<string | null>(null);
  const [recarga, setRecarga] = useState(0);

  const [corridas, setCorridas] = useState<RuntConfirmationRun[]>([]);
  const [tipos, setTipos] = useState<Array<{ code: string; name: string }>>([]);
  const [exportando, setExportando] = useState(false);
  const [exportAviso, setExportAviso] = useState<string | null>(null);

  // Tamaño de página como en /tramites: se lee con useSyncExternalStore porque sessionStorage no
  // existe en el render del servidor.
  const pageSizeGuardado = useSyncExternalStore(suscripcionInerte, leerPageSizeGuardado, () => PAGE_SIZE_POR_DEFECTO);
  const [pageSizeElegido, setPageSizeElegido] = useState<number | null>(null);
  const pageSize = pageSizeElegido ?? pageSizeGuardado;

  const aplicar = useCallback(
    (cambios: Partial<FiltrosUrl>) => {
      const siguiente: FiltrosUrl = { ...filtros, ...cambios };
      if (!("page" in cambios)) siguiente.page = 1;
      router.replace(`${pathname}${aQuery(siguiente)}`);
    },
    [filtros, pathname, router],
  );

  const filtroApi = useMemo<RuntConfirmationAttemptsFilter>(
    () => ({
      runId: filtros.runId || undefined,
      verdict: (filtros.verdict as RuntConfirmationVerdict) || undefined,
      procedureTypeCode: filtros.procedureTypeCode || undefined,
      procedureInstanceId: filtros.procedureInstanceId || undefined,
      search: filtros.search || undefined,
    }),
    [filtros],
  );

  // Listado: se recarga con cada cambio de filtro/página de la URL y con `recarga` (tras una
  // consulta manual). El AbortController descarta la respuesta de una carga superada.
  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga vía API (patrón NotificacionesBankPanel)
    setEstado("loading");
    listRuntConfirmationAttempts(filtroApi, filtros.page, pageSize, controller.signal)
      .then((page) => {
        if (controller.signal.aborted) return;
        setRows(page.items);
        setTotal(page.total);
        setEstado("ready");
      })
      .catch((err) => {
        if (controller.signal.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
        setEstado("error");
      });
    return () => controller.abort();
  }, [filtroApi, filtros.page, pageSize, recarga]);

  // Catálogos de los filtros: últimas corridas y tipos de trámite. Fallar aquí no tumba la tabla.
  useEffect(() => {
    const controller = new AbortController();
    listRuntConfirmationRuns(1, 30, controller.signal)
      .then((p) => {
        if (!controller.signal.aborted) setCorridas(p.items);
      })
      .catch(() => undefined);
    superadminClient
      .listProcedureTypes()
      .then((items) => {
        if (!controller.signal.aborted) setTipos(items.map((t) => ({ code: t.code, name: t.name })));
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, []);

  const corridaFiltrada = useMemo(
    () => (filtros.runId ? (corridas.find((r) => r.id === filtros.runId) ?? null) : null),
    [corridas, filtros.runId],
  );

  const exportar = useCallback(async () => {
    setExportando(true);
    setExportAviso(null);
    try {
      const primera = await listRuntConfirmationAttempts(filtroApi, 1, RUNT_ATTEMPTS_MAX_PAGE_SIZE);
      const sello = new Date().toISOString().slice(0, 16).replace(/[-:T]/g, "");
      const { exportadas, archivos } = await exportarPorLotes<RuntConfirmationAttemptRow>({
        total: primera.total,
        pageSize: RUNT_ATTEMPTS_MAX_PAGE_SIZE,
        traerPagina: async (page, pageSize) =>
          page === 1 ? primera.items : (await listRuntConfirmationAttempts(filtroApi, page, pageSize)).items,
        volcar: (lote, parte) => {
          const sufijo = parte.total > 1 ? `-parte-${parte.numero}-de-${parte.total}` : "";
          download(
            buildWorkbook("Confirmación RUNT", HISTORIAL_EXPORT_COLUMNS, lote, HISTORIAL_EXPORT_IDS),
            `confirmacion-runt-historial-${sello}${sufijo}.xlsx`,
            XLSX_MIME,
          );
        },
      });
      setExportAviso(
        exportadas === 0
          ? "No hay intentos que exportar con estos filtros."
          : archivos > 1
            ? `Se exportaron ${exportadas} intentos en ${archivos} archivos de hasta ${EXPORT_BATCH_SIZE} filas.`
            : `Se exportaron ${exportadas} intentos.`,
      );
    } catch (err) {
      toast.show(err instanceof Error ? `No se pudo exportar: ${err.message}` : "No se pudo exportar.", "error");
    } finally {
      setExportando(false);
    }
  }, [filtroApi, toast]);

  const columnas: DataTableColumn<RuntConfirmationAttemptRow>[] = useMemo(
    () => [
      {
        key: "fecha",
        header: "Fecha y hora",
        render: (r) => <span className="whitespace-nowrap tabular-nums">{formatFechaHora(r.queriedAt)}</span>,
      },
      {
        key: "tramite",
        header: "Trámite",
        render: (r) => (
          <span className="flex flex-col">
            <span className="font-mono text-xs font-semibold text-[#162744] dark:text-white">{r.referenceNumber}</span>
            <span className="text-[10px] opacity-60">{r.tenantName ?? "—"}</span>
          </span>
        ),
      },
      { key: "tipo", header: "Tipo", render: (r) => <span className="text-xs">{r.procedureTypeName}</span> },
      {
        key: "vehiculo",
        header: "Placa / VIN",
        render: (r) => <span className="font-mono text-xs">{vehiculoDe(r)}</span>,
      },
      { key: "proveedor", header: "Proveedor", render: (r) => <span className="text-xs">{proveedorLabel(r.providerKey)}</span> },
      { key: "intento", header: "Intento", align: "center", render: (r) => <span className="tabular-nums">{r.attemptNo}</span> },
      {
        key: "resultado",
        header: "Resultado",
        render: (r) => <StatusBadge label={RUNT_VERDICT_LABEL[r.verdict] ?? r.verdict} tone={VERDICT_TONE[r.verdict] ?? "neutral"} />,
      },
      {
        key: "ver",
        header: "",
        align: "right",
        render: (r) => (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              setExpandido((cur) => (cur === r.id ? null : r.id));
            }}
            aria-expanded={expandido === r.id}
            aria-controls={`intento-detalle-${r.id}`}
            className="rounded-full border border-[#DFE5ED] px-3 py-1 text-[11px] font-semibold text-[#557EFF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10"
          >
            {expandido === r.id ? "Cerrar" : "Ver"}
          </button>
        ),
      },
    ],
    [expandido],
  );

  return (
    <div className="flex flex-col gap-4" data-testid="confirmacion-runt-historial">
      {/* Filtros */}
      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          aplicar({ search: busqueda.trim() });
        }}
        aria-label="Filtros del historial"
      >
        <label className="flex flex-col gap-1 text-[11px] font-semibold text-[#162744] dark:text-white">
          Corrida
          <select className={selectCls} value={filtros.runId} onChange={(e) => aplicar({ runId: e.target.value })}>
            <option value="">Todas</option>
            {corridas.map((r) => (
              <option key={r.id} value={r.id}>
                {formatFechaHora(r.startedAt)} · {r.trigger === "manual" ? "manual" : "programada"}
                {r.skippedReason ? " · saltada" : ` · ${r.consulted} consultados`}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-[11px] font-semibold text-[#162744] dark:text-white">
          Resultado
          <select className={selectCls} value={filtros.verdict} onChange={(e) => aplicar({ verdict: e.target.value })}>
            <option value="">Todos</option>
            {VERDICTS.map((v) => (
              <option key={v} value={v}>
                {RUNT_VERDICT_LABEL[v]}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-[11px] font-semibold text-[#162744] dark:text-white">
          Tipo de trámite
          <select className={selectCls} value={filtros.procedureTypeCode} onChange={(e) => aplicar({ procedureTypeCode: e.target.value })}>
            <option value="">Todos</option>
            {tipos.map((t) => (
              <option key={t.code} value={t.code}>
                {t.name}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-[11px] font-semibold text-[#162744] dark:text-white">
          Trámite o placa
          <input
            type="search"
            value={busqueda}
            onChange={(e) => setBusqueda(e.target.value)}
            placeholder="Radicado, placa o VIN"
            className={`${selectCls} min-w-[180px]`}
          />
        </label>
        <button
          type="submit"
          className="rounded-full bg-gradient-to-r from-[#22D3C5] to-[#557EFF] px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
        >
          Buscar
        </button>
        {filtros.runId || filtros.verdict || filtros.procedureTypeCode || filtros.search || filtros.procedureInstanceId ? (
          <button
            type="button"
            onClick={() => {
              setBusqueda("");
              router.replace(pathname);
            }}
            className="text-xs font-semibold text-[#557EFF] underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          >
            Limpiar filtros
          </button>
        ) : null}
        <span className="ml-auto flex items-center gap-2">
          <button
            type="button"
            onClick={() => setRecarga((n) => n + 1)}
            className="flex items-center gap-1 rounded-full border border-[#DFE5ED] px-3 py-2 text-xs font-semibold text-[#162744] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:text-white"
          >
            <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
            Actualizar
          </button>
          <button
            type="button"
            onClick={() => void exportar()}
            disabled={exportando || estado === "loading"}
            data-testid="confirmacion-runt-export"
            className="flex items-center gap-1 rounded-full border border-[#DFE5ED] px-3 py-2 text-xs font-semibold text-[#162744] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:border-white/10 dark:text-white"
          >
            <Download className={`h-3.5 w-3.5 ${exportando ? "animate-pulse" : ""}`} aria-hidden="true" />
            {exportando ? "Exportando…" : "Exportar"}
          </button>
        </span>
      </form>

      {exportAviso ? (
        <p role="status" className="text-xs text-[#59677D] dark:text-white/60">
          {exportAviso}
        </p>
      ) : null}

      {filtros.procedureInstanceId ? (
        <InlineAlert tone="info" compact>
          Mostrando el historial completo de intentos del trámite seleccionado.
        </InlineAlert>
      ) : null}

      {/* Resumen de la corrida filtrada (AC2) */}
      {filtros.runId ? <UltimaCorridaResumen run={corridaFiltrada} titulo="Resumen de la corrida" /> : null}

      <DataTable<RuntConfirmationAttemptRow>
        columns={columnas}
        rows={rows}
        getRowKey={(r) => r.id}
        onRowClick={(r) => setExpandido((cur) => (cur === r.id ? null : r.id))}
        status={estado === "loading" ? "loading" : estado === "error" ? "error" : undefined}
        onRetry={() => setRecarga((n) => n + 1)}
        errorMessage="No se pudo cargar el historial de confirmación."
        emptyMessage={
          filtros.runId || filtros.verdict || filtros.procedureTypeCode || filtros.search || filtros.procedureInstanceId
            ? "Ningún intento coincide con estos filtros."
            : "Todavía no hay intentos de confirmación. Corren a la hora configurada o con «Consultar ahora»."
        }
        ariaLabel="Historial de intentos de confirmación RUNT"
        minWidth={960}
        pagination={{
          page: filtros.page,
          pageSize,
          totalCount: total,
          onPageChange: (page) => aplicar({ page }),
          pageSizeOptions: TAMANOS_DE_PAGINA,
          onPageSizeChange: (n) => {
            setPageSizeElegido(n);
            try {
              sessionStorage.setItem(CLAVE_PAGE_SIZE, String(n));
            } catch {
              // ventana privada: se queda en memoria y ya
            }
            if (filtros.page !== 1) aplicar({ page: 1 });
          },
        }}
        renderExpanded={(r) =>
          expandido === r.id ? (
            <IntentoDetalle
              row={r}
              onNuevoIntento={() => {
                setExpandido(null);
                setRecarga((n) => n + 1);
                if (filtros.page !== 1) aplicar({ page: 1 });
              }}
              onVerTramite={() => aplicar({ procedureInstanceId: r.procedureInstanceId, search: "", runId: "", verdict: "", procedureTypeCode: "" })}
            />
          ) : null
        }
      />
    </div>
  );
}
