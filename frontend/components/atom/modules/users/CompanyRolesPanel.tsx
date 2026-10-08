"use client";

// HU #13443 (Feature #13437, Épica #12750) — pestaña «Roles y permisos» del módulo Usuarios para el Admin de
// Compañía: crea, edita y elimina los roles propios de su compañía y ve los globales de FLIT en solo lectura.
// Patrón de tabla de trámites (table-styles, RowActions, Pagination numerada con «Filas por página»), modal normal
// y los 4 estados (cargando, error, vacío, lleno). Backend: HU #13441 (/api/v1/security/roles).
import { useCallback, useEffect, useState } from "react";
import { Eye, Loader2, Pencil, Plus, Trash2 } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { Modal } from "@/components/atom/Modal";
import { Pagination } from "@/components/atom/Pagination";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import { deleteTenantRole, getRoles, type TenantRole } from "@/lib/api/security";
import { CompanyRoleFormModal, type CompanyRoleFormMode } from "./CompanyRoleFormModal";
import { companyRoleErrorMessage } from "./companyRoleErrors";

const COLUMNS = ["Código", "Nombre", "Tipo", "Permisos", "Acciones"];

/** Un rol es propio de la compañía si trae `tenantId`; sin él es del catálogo global de FLIT. */
export function isOwnRole(role: TenantRole): boolean {
  return role.tenantId != null;
}

export function CompanyRolesPanel() {
  const [roles, setRoles] = useState<TenantRole[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [form, setForm] = useState<CompanyRoleFormMode | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<TenantRole | null>(null);
  const pg = usePaginacion();

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const items = await getRoles();
      setRoles(items);
      setStatus(items.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
  }, [load]);

  function actionsFor(role: TenantRole): RowAction[] {
    if (!isOwnRole(role)) {
      return [{ icon: Eye, label: `Ver permisos de ${role.name}`, onClick: () => setForm({ kind: "view", role }) }];
    }
    return [
      { icon: Pencil, label: `Editar ${role.name}`, tone: "primary", onClick: () => setForm({ kind: "edit", role }) },
      { icon: Trash2, label: `Eliminar ${role.name}`, tone: "danger", onClick: () => setDeleteTarget(role) },
    ];
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-3xl text-sm text-[#59677D] dark:text-white/78">
          Crea los roles de tu compañía y define sus permisos. Los roles de FLIT se pueden asignar, pero no cambiar.
        </p>
        <button
          type="button"
          onClick={() => setForm({ kind: "create" })}
          className="inline-flex shrink-0 items-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold text-white shadow-[0_10px_22px_rgba(79,116,201,0.22)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ background: "linear-gradient(135deg, #557EFF 0%, #00DBD5 100%)" }}
        >
          <Plus className="h-4 w-4" aria-hidden="true" />
          Nuevo rol
        </button>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="No hay roles todavía. Crea el primero con «Nuevo rol»."
        errorMessage="No se pudieron cargar los roles."
        onRetry={() => void load()}
        skeletonRows={3}
      >
        <div className="overflow-x-auto">
          <table
            aria-label="Roles de la compañía"
            style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px", minWidth: 560 }}
            className="text-xs"
          >
            <thead>
              <tr>
                {COLUMNS.map((col, i, arr) => (
                  <th
                    key={col}
                    scope="col"
                    className={`${TABLA_HEADER_CELL_CLS} ${i === 0 ? "rounded-l-xl" : ""} ${i === arr.length - 1 ? "rounded-r-xl text-center" : ""}`}
                    style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                  >
                    {col}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {pg.paginar(roles).map((r) => (
                <tr key={r.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                  <td className="rounded-l-xl border-y border-l px-4 py-3 font-mono opacity-80" style={{ borderColor: "#DFE5ED" }}>
                    {r.code}
                  </td>
                  <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                    <span className="block font-semibold">{r.name}</span>
                    {r.description && <span className="block opacity-70">{r.description}</span>}
                  </td>
                  <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                    {isOwnRole(r) ? (
                      <StatusBadge label="Propio" tone="success" ariaLabel="Rol propio de tu compañía" />
                    ) : (
                      <StatusBadge label="FLIT · solo lectura" tone="neutral" ariaLabel="Rol global de FLIT, solo lectura" />
                    )}
                  </td>
                  <td className="border-y px-4 py-3 text-center font-bold" style={{ borderColor: "#DFE5ED", color: "#557EFF" }}>
                    {r.permissionCount}
                  </td>
                  <td className="rounded-r-xl border-y border-r px-4 py-3 text-center" style={{ borderColor: "#DFE5ED" }}>
                    <RowActions actions={actionsFor(r)} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination
          page={pg.page}
          pageSize={pg.pageSize}
          totalCount={roles.length}
          onPageChange={pg.setPage}
          onPageSizeChange={pg.setPageSize}
          noun="roles"
          ariaLabel="Paginación de roles"
        />
      </UiStateBoundary>

      {form && (
        <CompanyRoleFormModal
          mode={form}
          onClose={() => setForm(null)}
          onSaved={() => {
            setForm(null);
            void load();
          }}
        />
      )}

      {deleteTarget && (
        <DeleteCompanyRoleDialog
          role={deleteTarget}
          onClose={() => setDeleteTarget(null)}
          onDeleted={() => {
            setDeleteTarget(null);
            void load();
          }}
        />
      )}
    </div>
  );
}

function DeleteCompanyRoleDialog({
  role,
  onClose,
  onDeleted,
}: {
  role: TenantRole;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function confirm() {
    setBusy(true);
    setError(null);
    try {
      await deleteTenantRole(role.id);
      onDeleted();
    } catch (err) {
      // 409: el rol está asignado a usuarios; se explica qué hacer en vez de un error genérico.
      setError(companyRoleErrorMessage(err));
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={() => {
        if (!busy) onClose();
      }}
      busy={busy}
      icon={Trash2}
      iconBg="#FF4E00"
      title="Eliminar rol"
      description={role.code}
    >
      <div className="space-y-3.5">
        <p className="text-sm">
          ¿Seguro que deseas eliminar el rol <strong>{role.name}</strong>? No se puede eliminar si hay usuarios con este rol.
        </p>
        {error && (
          <p role="alert" className="rounded-lg px-3 py-2 text-xs font-medium" style={{ background: "rgba(255,78,0,0.1)", color: "#FF4E00" }}>
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void confirm()}
            disabled={busy}
            className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "#FF4E00" }}
          >
            {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
            Eliminar
          </button>
        </div>
      </div>
    </Modal>
  );
}
