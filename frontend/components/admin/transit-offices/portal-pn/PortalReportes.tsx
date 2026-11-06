"use client";

// HU #14399 (Feature #14396, Épica #14297) — Reporte de los trámites que caen por el portal Persona Natural. Es UN solo
// componente para el hub del OT (subpestaña «Reportes», la primera) y para Plataforma (pestaña «Reportes»): cambia el
// cliente inyectado (`api`) y si se ofrece el filtro de organismo (solo el SuperAdmin en Plataforma). El servidor decide el
// alcance; aquí solo se pintan los datos tal cual llegan: lo que no viene (sin pago aún) queda vacío, sin inventar.
import { useCallback, useEffect, useMemo, useState } from "react";
import { Download } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import {
  EncabezadoFiltrable,
  TablaFiltrosBar,
  TablaFiltrosVacio,
} from "@/components/atom/TablaFiltrosBar";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { PnButton } from "@/components/admin/plataforma/persona-natural/pn-componentes";
import { PN_FIELD_ERROR_CLS } from "@/components/admin/plataforma/persona-natural/pn-ui";
import { formatPesos } from "@/lib/api/admin-pn-tarifas";
import type {
  FilaReportePortal,
  FiltroReportePortal,
  PaginaReportePortal,
  ReportePortalApi,
} from "@/lib/api/admin-portal-reportes";
import { ApiError } from "@/lib/api/types";
import { formatFechaHora } from "@/lib/format/date";
import { condicionesAParams, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
import { canalLabel } from "@/lib/tramites/tramites-row-labels";

export const REPORTE_PORTAL_CARGANDO = "Armando el reporte…";
export const REPORTE_PORTAL_VACIO = "No hay trámites del portal en FLIT 2.0 con esos filtros.";
const ERROR_GENERICO = "No se pudo armar el reporte. Inténtalo de nuevo.";

/** El gestor nunca aparece en este reporte: solo los canales del portal y de la aplicación. */
const CANALES_REPORTE = ["pn_web", "pn_punto", "aplicacion"] as const;

type Opcion = { id: string; nombre: string };
type Fila = FilaReportePortal & { clave: string };

export interface PortalReportesProps {
  api: ReportePortalApi;
  /** `true` solo para el SuperAdmin en Plataforma; el OT no ve el filtro de organismo. */
  incluirFiltroOrganismo?: boolean;
}

function motivo(err: unknown): string {
  return err instanceof ApiError && err.message ? err.message : ERROR_GENERICO;
}

const texto = (v: string | null | undefined) => (v && v.trim() ? v : "");
const pesos = (v: number | null | undefined) =>
  v === null || v === undefined ? "" : formatPesos(v);

export function PortalReportes({ api, incluirFiltroOrganismo = false }: PortalReportesProps) {
  const [tipos, setTipos] = useState<Opcion[]>([]);
  const [organismos, setOrganismos] = useState<Opcion[]>([]);
  const [pagina, setPagina] = useState<PaginaReportePortal | null>(null);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [mensajeError, setMensajeError] = useState(ERROR_GENERICO);
  const [exportando, setExportando] = useState(false);
  const [errorExport, setErrorExport] = useState<string | null>(null);
  const [recarga, setRecarga] = useState(0);

  const pg = usePaginacion("portal.reportes");
  const { setPage } = pg;
  const volverAPrimeraPagina = useCallback(() => setPage(1), [setPage]);

  const columnasFiltro = useMemo<ColumnaFiltro[]>(() => {
    const cols: ColumnaFiltro[] = [
      {
        id: "canal",
        label: "Canal",
        tipo: "seleccion",
        opciones: CANALES_REPORTE.map((c) => ({ value: c, label: canalLabel(c) })),
      },
      {
        id: "tipo",
        label: "Tipo de trámite",
        tipo: "seleccion",
        param: "tipoTramiteId",
        opciones: tipos.map((t) => ({ value: t.id, label: t.nombre })),
      },
      {
        id: "fecha",
        label: "Fecha en que cayó",
        tipo: "fecha",
        paramDesde: "desde",
        paramHasta: "hasta",
      },
    ];
    if (incluirFiltroOrganismo) {
      cols.push({
        id: "organismo",
        label: "Organismo",
        tipo: "seleccion",
        param: "transitOfficeId",
        opciones: organismos.map((o) => ({ value: o.id, label: o.nombre })),
      });
    }
    return cols;
  }, [tipos, organismos, incluirFiltroOrganismo]);

  const filtros = useTablaFiltros({
    columnas: columnasFiltro,
    periodoColumnaId: "fecha",
    onCambio: volverAPrimeraPagina,
  });

  const filtroServidor = useMemo<FiltroReportePortal>(
    () => condicionesAParams(filtros.condicionesTotales, columnasFiltro) as FiltroReportePortal,
    [filtros.condicionesTotales, columnasFiltro],
  );

  // Se compara por valor: al llegar los catálogos cambian las columnas, pero no lo que se pide al servidor.
  const claveFiltro = JSON.stringify(filtroServidor);

  // Catálogos de los filtros: si fallan, el reporte sigue y solo queda sin opciones ese filtro.
  useEffect(() => {
    const controller = new AbortController();
    api
      .tipos(controller.signal)
      .then((t) => !controller.signal.aborted && setTipos(t))
      .catch(() => undefined);
    if (incluirFiltroOrganismo && api.organismos) {
      api
        .organismos(controller.signal)
        .then((o) => !controller.signal.aborted && setOrganismos(o))
        .catch(() => undefined);
    }
    return () => controller.abort();
  }, [api, incluirFiltroOrganismo]);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- consulta del servidor: el estado de carga es intencional
    setStatus("loading");
    api
      .listar({ ...(JSON.parse(claveFiltro) as FiltroReportePortal), page: pg.page, pageSize: pg.pageSize }, controller.signal)
      .then((res) => {
        if (controller.signal.aborted) return;
        setPagina(res);
        setStatus(res.items.length === 0 ? "empty" : "ready");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setMensajeError(motivo(err));
        setStatus("error");
      });
    return () => controller.abort();
  }, [api, claveFiltro, pg.page, pg.pageSize, recarga]);

  async function exportar() {
    setExportando(true);
    setErrorExport(null);
    try {
      await api.exportar(filtroServidor);
    } catch (err) {
      setErrorExport(motivo(err));
    } finally {
      setExportando(false);
    }
  }

  const filas: Fila[] = useMemo(
    () => (pagina?.items ?? []).map((f, i) => ({ ...f, clave: `${pagina?.page ?? 1}-${i}` })),
    [pagina],
  );

  const columns: DataTableColumn<Fila>[] = [
    {
      key: "canal",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="canal">
          Canal
        </EncabezadoFiltrable>
      ),
      render: (f) => canalLabel(f.canal),
    },
    {
      key: "organismo",
      header: incluirFiltroOrganismo ? (
        <EncabezadoFiltrable filtros={filtros} columnaId="organismo">
          Organismo
        </EncabezadoFiltrable>
      ) : (
        "Organismo"
      ),
      render: (f) => texto(f.organismo),
    },
    {
      key: "tipo",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="tipo">
          Tipo
        </EncabezadoFiltrable>
      ),
      render: (f) => f.tipo,
    },
    { key: "radicado", header: "Radicado", render: (f) => texto(f.radicado) },
    { key: "estado", header: "Estado", render: (f) => f.estado },
    {
      key: "fecha",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="fecha">
          Fecha en que cayó
        </EncabezadoFiltrable>
      ),
      render: (f) => formatFechaHora(f.fechaCaida, ""),
    },
    { key: "modalidad", header: "Modalidad", render: (f) => texto(f.modalidad) },
    {
      key: "valorTramite",
      header: "Valor del trámite",
      align: "right",
      render: (f) => pesos(f.valorTramite),
    },
    {
      key: "valorGestion",
      header: "Valor de la gestión",
      align: "right",
      render: (f) => pesos(f.valorGestion),
    },
  ];

  return (
    <section aria-labelledby="portal-reportes-titulo" className="flex flex-col gap-3">
      <div>
        <h2
          id="portal-reportes-titulo"
          className="text-base font-bold text-flit-primary dark:text-white"
        >
          Reportes
        </h2>
        <p className="text-xs text-[#59677D] dark:text-white/70">
          Trámites que llegaron por el portal Persona Natural y por la aplicación. Los valores
          aparecen cuando el trámite ya tiene pago.
        </p>
      </div>

      <TablaFiltrosBar
        filtros={filtros}
        acciones={
          <PnButton
            variant="secondary"
            icon={Download}
            loading={exportando}
            onClick={() => void exportar()}
          >
            Exportar a Excel
          </PnButton>
        }
      />
      {errorExport && (
        <p role="alert" className={PN_FIELD_ERROR_CLS}>
          {errorExport}
        </p>
      )}

      {status === "loading" ? (
        <CarLoaderModal label={REPORTE_PORTAL_CARGANDO} />
      ) : (
        <UiStateBoundary
          status={status === "empty" ? "ready" : status}
          errorMessage={mensajeError}
          onRetry={() => setRecarga((n) => n + 1)}
        >
          <DataTable<Fila>
            ariaLabel="Trámites del portal Persona Natural"
            columns={columns}
            rows={filas}
            getRowKey={(f) => f.clave}
            emptyContent={
              filtros.hayFiltros ? (
                <TablaFiltrosVacio onLimpiar={filtros.limpiar} mensaje={REPORTE_PORTAL_VACIO} />
              ) : (
                <p
                  role="status"
                  className="px-4 py-10 text-center text-sm font-semibold text-flit-primary dark:text-white"
                >
                  {REPORTE_PORTAL_VACIO}
                </p>
              )
            }
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: pagina?.totalCount ?? 0,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      )}
    </section>
  );
}
