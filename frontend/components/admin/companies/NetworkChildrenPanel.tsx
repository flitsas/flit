"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Mail, Settings2, UserPlus } from "lucide-react";
import { CreateButton } from "@/components/atom/CreateButton";
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { RowActions } from "@/components/atom/RowActions";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { InviteUserModal } from "@/components/atom/modules/users/InviteUserModal";
import { createChildCompany, fetchCompanyChildren, inviteChildCompanyUser } from "@/lib/api/admin-companies";
import { getRoles, type TenantRole } from "@/lib/api/security";
import { tenantTypeLabel } from "@/lib/api/types";
import type { CompanyChildListItem, CompanyListItem } from "@/lib/api/types";
import { ChildCompanyStatusDialog } from "./ChildCompanyStatusDialog";
import { CreateChildCompanyDialog } from "./CreateChildCompanyDialog";

import { formatFechaHora } from "@/lib/format/date";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
export interface NetworkChildrenPanelProps {

  headTenantId: string;
  headTenantType: string;
}

/** HU #12356 — panel de red para cabeza de grupo. */
export function NetworkChildrenPanel({ headTenantId, headTenantType }: NetworkChildrenPanelProps) {
  const router = useRouter();
  const [status, setStatus] = useState<UiStatus>("loading");
  const pg = usePaginacion();
  const [children, setChildren] = useState<CompanyChildListItem[]>([]);
  const [createOpen, setCreateOpen] = useState(false);
  const [statusTarget, setStatusTarget] = useState<CompanyChildListItem | null>(null);
  const [inviteTarget, setInviteTarget] = useState<CompanyChildListItem | null>(null);
  const [roles, setRoles] = useState<TenantRole[]>([]);
  const [rolesLoading, setRolesLoading] = useState(false);

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const data = await fetchCompanyChildren(headTenantId, signal);
      if (signal?.aborted) return;
      setChildren(data);
      setStatus(data.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) {
        setStatus("error");
      }
    }
  }, [headTenantId]);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  useEffect(() => {
    if (!inviteTarget) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setRolesLoading(true);
    void getRoles()
      .then((list) => {
        const allowed = new Set([
          "admincompany",
          "radicador",
          "gestor",
          "documentador",
          "validador",
          "operariofull",
        ]);
        setRoles(list.filter((r) => allowed.has(r.code.toLowerCase())));
      })
      .catch(() => setRoles([]))
      .finally(() => setRolesLoading(false));
  }, [inviteTarget]);

  const handleCreated = (created: CompanyListItem) => {
    setCreateOpen(false);
    setChildren((prev) => [
      {
        id: created.id,
        nit: created.nit,
        razonSocial: created.razonSocial,
        code: created.code,
        tenantType: created.tenantType,
        estadoActivo: created.estadoActivo,
        fechaVinculacion: created.fechaCreacion,
        rowVersion: created.rowVersion,
      },
      ...prev,
    ]);
    setStatus("ready");
  };

  const handleStatusConfirmed = (updated: CompanyChildListItem) => {
    setChildren((prev) => prev.map((c) => (c.id === updated.id ? updated : c)));
    setStatusTarget(null);
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-lg font-bold">Red de clientes</h1>
          <p className="text-xs opacity-70">
            Administra los clientes hijos de tu cabeza de grupo. No incluye vincular clientes existentes
            (solo el SuperAdmin puede hacerlo).
          </p>
        </div>
        <CreateButton label="Agregar cliente a la red" icon={UserPlus} onClick={() => setCreateOpen(true)} />
      </div>

      {status === "loading" ? (
        <CarLoaderModal label="Cargando tu red de clientes…" />
      ) : (
      <UiStateBoundary
        status={status}
        onRetry={() => void load()}
        emptyMessage="Aún no tienes clientes en tu red. Crea el primero."
        emptyCta={
          <button
            type="button"
            onClick={() => setCreateOpen(true)}
            className="mt-3 rounded-xl px-4 py-2 text-xs font-semibold text-white"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            Agregar cliente a la red
          </button>
        }
        errorMessage="No se pudo cargar tu red de clientes."
        skeletonRows={4}
      >
        <div className="overflow-x-auto">
          {/* Bug #13055 — tabla homologada con el modelo de trámites */}
<table
 aria-label="Red de clientes"
 className="min-w-[720px] text-xs"
 style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
>
            <thead>
              <tr>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Cliente
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
                          icon: Settings2,
                          label: `Abrir configuración de ${child.razonSocial}`,
                          tone: "primary",
                          onClick: () =>
                            router.push(
                              `/admin/companies/${child.id}?networkHead=${headTenantId}`,
                            ),
                        },
                        {
                          icon: Mail,
                          label: `Invitar usuario a ${child.razonSocial}`,
                          tone: "primary",
                          onClick: () => setInviteTarget(child),
                        },
                        {
                          icon: UserPlus,
                          label: child.estadoActivo
                            ? `Desactivar ${child.razonSocial}`
                            : `Activar ${child.razonSocial}`,
                          tone: child.estadoActivo ? "danger" : "primary",
                          onClick: () => setStatusTarget(child),
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

      <CreateChildCompanyDialog
        open={createOpen}
        headTenantId={headTenantId}
        headTenantType={headTenantType}
        onClose={() => setCreateOpen(false)}
        onCreate={createChildCompany}
        onCreated={handleCreated}
      />

      {statusTarget && (
        <ChildCompanyStatusDialog
          headTenantId={headTenantId}
          child={statusTarget}
          onClose={() => setStatusTarget(null)}
          onConfirmed={handleStatusConfirmed}
        />
      )}

      {inviteTarget && (
        <InviteUserModal
          onClose={() => setInviteTarget(null)}
          onSuccess={() => setInviteTarget(null)}
          isSuperAdmin={false}
          roles={roles}
          rolesLoading={rolesLoading}
          fixedTarget={{
            tenantId: inviteTarget.id,
            profile: "GESTOR",
            name: inviteTarget.razonSocial,
          }}
          submitInvitation={({ email, fullName, roleIds }) =>
            inviteChildCompanyUser(headTenantId, inviteTarget.id, { email, fullName, roleIds })
          }
        />
      )}
    </div>
  );
}

function formatDate(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return formatFechaHora(parsed);
}
