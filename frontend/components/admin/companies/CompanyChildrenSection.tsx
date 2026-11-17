"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Unlink } from "lucide-react";
import { iconoComo } from "@flit/ui/iconoComo";
import { CreateButton } from "@/components/atom/CreateButton";
import { RowActions } from "@/components/atom/RowActions";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchCompanyChildren } from "@/lib/api/admin-companies";
import { isHeadTenantType, tenantTypeLabel } from "@/lib/api/types";
import type { CompanyChildListItem, CompanyListItem } from "@/lib/api/types";
import { TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { EncabezadoFiltrable } from "@/components/atom/FiltroColumna";
import { filtrarFilas, type AccesoresFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
import { columnasHijas } from "./company-filter-columns";
import { LinkCompanyDialog } from "./LinkCompanyDialog";
import { UnlinkCompanyDialog } from "./UnlinkCompanyDialog";

import { formatFechaHora } from "@/lib/format/date";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_FILA_ANCHA_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
  TABLA_ROW_HOVER_CLS,
  TABLA_TARJETA_CLS,
} from "@/components/atom/table-styles";
const COLUMNAS = columnasHijas("Razón social");
const ACCESORES: AccesoresFiltro<CompanyChildListItem> = {
  columnas: {
    razonSocial: (c) => c.razonSocial,
    nit: (c) => c.nit,
    tipo: (c) => c.tenantType,
    estado: (c) => String(c.estadoActivo),
    vinculacion: (c) => c.fechaVinculacion,
  },
};

export interface CompanyChildrenSectionProps {
  company: CompanyListItem;
}

/** HU #12357 — sección «Clientes hijos» en ficha SuperAdmin de cabeza de grupo. */
export function CompanyChildrenSection({ company }: CompanyChildrenSectionProps) {
  const [status, setStatus] = useState<UiStatus>("loading");
  const pg = usePaginacion("companies.CompanyChildrenSection");
  // HU #13999 — filtros por columna en cliente (estándar HU #13997).
  const { setPage } = pg;
  const filtros = useTablaFiltros({
    columnas: COLUMNAS,
    onCambio: () => setPage(1),
  });
  const [children, setChildren] = useState<CompanyChildListItem[]>([]);
  const [linkOpen, setLinkOpen] = useState(false);
  const [unlinkTarget, setUnlinkTarget] = useState<CompanyChildListItem | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const data = await fetchCompanyChildren(company.id, signal);
        if (signal?.aborted) return;
        setChildren(data);
        setStatus(data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [company.id],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const visibles = useMemo(
    () => filtrarFilas(children, filtros.condicionesTotales, filtros.busqueda, ACCESORES),
    [children, filtros.condicionesTotales, filtros.busqueda],
  );

  if (!isHeadTenantType(company.tenantType) && !company.isGroupParent) {
    return null;
  }

  const activeCount = children.filter((c) => c.estadoActivo).length;

  return (
    <section className="mt-6" aria-labelledby="children-section-title">
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 id="children-section-title" className="text-sm font-bold">
            Clientes hijos
          </h2>
          <p className="text-xs opacity-60">
            {company.tenantType === "MARCA_BLANCA"
              ? "Clientes vinculados a esta Marca Blanca."
              : "Clientes de la Concesión vinculados por el SuperAdmin."}
          </p>
        </div>
        <CreateButton
          label="Vincular cliente existente"
          icon={iconoComo("externo")}
          onClick={() => setLinkOpen(true)}
        />
      </div>

      {status === "loading" ? (
        <CarLoaderModal label="Cargando clientes hijos…" />
      ) : (
        <UiStateBoundary
          status={status}
          onRetry={() => void load()}
          emptyMessage="Aún no hay clientes vinculados a esta cabeza de grupo."
          errorMessage="No se pudo cargar el listado de clientes hijos."
          skeletonRows={3}
        >
          {(
            <div className={TABLA_TARJETA_CLS}>
              <div className="overflow-x-auto">
                {/* Bug #13055 — tabla homologada con el modelo de trámites */}
                <table
                  aria-label="Clientes hijos"
                  className={`min-w-[560px] text-xs ${TABLA_MOVIL_TABLE_CLS}`}
                  style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
                >
                  <thead className={TABLA_MOVIL_THEAD_CLS}>
                    <tr>
                      <th
                        scope="col"
                        className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`}
                        style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                      >
                        <EncabezadoFiltrable filtros={filtros} columnaId="razonSocial">
                          Razón social
                        </EncabezadoFiltrable>
                      </th>
                      <th
                        scope="col"
                        className={`${TABLA_HEADER_CELL_CLS}`}
                        style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                      >
                        <EncabezadoFiltrable filtros={filtros} columnaId="nit">
                          NIT
                        </EncabezadoFiltrable>
                      </th>
                      <th
                        scope="col"
                        className={`${TABLA_HEADER_CELL_CLS}`}
                        style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                      >
                        <EncabezadoFiltrable filtros={filtros} columnaId="tipo">
                          Tipo
                        </EncabezadoFiltrable>
                      </th>
                      <th
                        scope="col"
                        className={`${TABLA_HEADER_CELL_CLS}`}
                        style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                      >
                        <EncabezadoFiltrable filtros={filtros} columnaId="estado">
                          Estado
                        </EncabezadoFiltrable>
                      </th>
                      <th
                        scope="col"
                        className={`${TABLA_HEADER_CELL_CLS}`}
                        style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                      >
                        <EncabezadoFiltrable filtros={filtros} columnaId="vinculacion">
                          Vinculación
                        </EncabezadoFiltrable>
                      </th>
                      <th
                        scope="col"
                        className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`}
                        style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                      >
                        Acciones
                      </th>
                    </tr>
                  </thead>
                  <tbody className={TABLA_MOVIL_TBODY_CLS}>
                    {visibles.length === 0 && filtros.hayFiltros ? (
                      <tr className={TABLA_MOVIL_FILA_ANCHA_CLS}>
                        <td colSpan={6} className={TABLA_MOVIL_FILA_ANCHA_CLS}>
                          <TablaFiltrosVacio onLimpiar={filtros.limpiar} />
                        </td>
                      </tr>
                    ) : null}
                    {pg.paginar(visibles).map((child) => (
                      <tr
                        key={child.id}
                        className={`bg-card ${TABLA_ROW_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}
                      >
                        <td
                          data-label="Razón social"
                          className={`rounded-l-xl border-y border-l px-4 py-3 font-semibold ${TABLA_MOVIL_CELL_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS}`}
                          style={{ borderColor: "var(--border)" }}
                        >
                          {child.razonSocial}
                        </td>
                        <td
                          data-label="NIT"
                          className={`border-y px-4 py-3 font-mono ${TABLA_MOVIL_CELL_CLS}`}
                          style={{ borderColor: "var(--border)" }}
                        >
                          {child.nit}
                        </td>
                        <td data-label="Tipo" className={`border-y px-4 py-3 ${TABLA_MOVIL_CELL_CLS}`} style={{ borderColor: "var(--border)" }}>
                          {tenantTypeLabel(child.tenantType)}
                        </td>
                        <td data-label="Estado" className={`border-y px-4 py-3 ${TABLA_MOVIL_CELL_CLS}`} style={{ borderColor: "var(--border)" }}>
                          {child.estadoActivo ? (
                            <StatusBadge label="Activo" tone="success" />
                          ) : (
                            <StatusBadge label="Inactivo" tone="danger" />
                          )}
                        </td>
                        <td
                          data-label="Vinculación"
                          className={`border-y px-4 py-3 opacity-70 ${TABLA_MOVIL_CELL_CLS}`}
                          style={{ borderColor: "var(--border)" }}
                        >
                          {formatDate(child.fechaVinculacion)}
                        </td>
                        <td
                          data-label="Acciones"
                          className={`rounded-r-xl border-y border-r px-4 py-3 text-right ${TABLA_MOVIL_CELL_CLS}`}
                          style={{ borderColor: "var(--border)" }}
                        >
                          <RowActions
                            actions={[
                              {
                                icon: Unlink,
                                label: `Desvincular ${child.razonSocial}`,
                                onClick: () => setUnlinkTarget(child),
                                tone: "danger",
                              },
                            ]}
                          />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {/* Bug #13055 — paginación en cliente con filas por página, como el listado de trámites */}
              {visibles.length > 0 && (
                <Pagination
                  page={pg.page}
                  pageSize={pg.pageSize}
                  totalCount={visibles.length}
                  onPageChange={pg.setPage}
                  onPageSizeChange={pg.setPageSize}
                  noun="clientes"
                />
              )}
            </div>
          )}
        </UiStateBoundary>
      )}

      {linkOpen && (
        <LinkCompanyDialog
          open
          headTenantId={company.id}
          headName={company.razonSocial}
          linkedChildIds={children.map((c) => c.id)}
          onClose={() => setLinkOpen(false)}
          onLinked={() => {
            setLinkOpen(false);
            void load();
          }}
        />
      )}

      {unlinkTarget && (
        <UnlinkCompanyDialog
          child={unlinkTarget}
          headName={company.razonSocial}
          onClose={() => setUnlinkTarget(null)}
          onUnlinked={() => {
            setUnlinkTarget(null);
            void load();
          }}
        />
      )}

      <p className="sr-only" aria-live="polite">
        {activeCount} clientes hijos activos
      </p>
    </section>
  );
}

function formatDate(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return formatFechaHora(parsed);
}
