"use client";

import { useCallback, useEffect, useState } from "react";
import { Link2, Unlink } from "lucide-react";
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
import { LinkCompanyDialog } from "./LinkCompanyDialog";
import { UnlinkCompanyDialog } from "./UnlinkCompanyDialog";

import { formatFechaHora } from "@/lib/format/date";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
export interface CompanyChildrenSectionProps {

  company: CompanyListItem;
}

/** HU #12357 — sección «Clientes hijos» en ficha SuperAdmin de cabeza de grupo. */
export function CompanyChildrenSection({ company }: CompanyChildrenSectionProps) {
  const [status, setStatus] = useState<UiStatus>("loading");
  const pg = usePaginacion();
  const [children, setChildren] = useState<CompanyChildListItem[]>([]);
  const [linkOpen, setLinkOpen] = useState(false);
  const [unlinkTarget, setUnlinkTarget] = useState<CompanyChildListItem | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
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
  }, [company.id]);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  if (!isHeadTenantType(company.tenantType) && !company.isGroupParent) {
    return null;
  }

  const activeCount = children.filter((c) => c.estadoActivo).length;

  return (
    <section className="mt-6" aria-labelledby="children-section-title"
    >
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
        <CreateButton label="Vincular cliente existente" icon={Link2} onClick={() => setLinkOpen(true)} />
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
        <div className="overflow-x-auto">
          {/* Bug #13055 — tabla homologada con el modelo de trámites */}
<table
 aria-label="Clientes hijos"
 className="min-w-[560px] text-xs"
 style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
>
            <thead>
              <tr>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Razón social
                </th>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS}`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  NIT
                </th>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS}`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Tipo
                </th>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS}`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Estado
                </th>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS}`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Vinculación
                </th>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Acciones
                </th>
              </tr>
            </thead>
            <tbody>
              {pg.paginar(children).map((child) => (
                <tr key={child.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                  <td className="rounded-l-xl border-y border-l px-4 py-3 font-semibold" style={{ borderColor: "#DFE5ED" }}>{child.razonSocial}</td>
                  <td className="border-y px-4 py-3 font-mono" style={{ borderColor: "#DFE5ED" }}>{child.nit}</td>
                  <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>{tenantTypeLabel(child.tenantType)}</td>
                  <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                    {child.estadoActivo ? (
                      <StatusBadge label="Activo" tone="success" />
                    ) : (
                      <StatusBadge label="Inactivo" tone="danger" />
                    )}
                  </td>
                  <td className="border-y px-4 py-3 opacity-70" style={{ borderColor: "#DFE5ED" }}>{formatDate(child.fechaVinculacion)}</td>
                  <td className="rounded-r-xl border-y border-r px-4 py-3 text-right" style={{ borderColor: "#DFE5ED" }}>
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
        <Pagination
          page={pg.page}
          pageSize={pg.pageSize}
          totalCount={children.length}
          onPageChange={pg.setPage}
          onPageSizeChange={pg.setPageSize}
          noun="clientes"
        />

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
