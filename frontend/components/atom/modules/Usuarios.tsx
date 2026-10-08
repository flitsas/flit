"use client";

import { useState, useEffect } from "react";
import Link from "next/link";
import { Users, Shield, Ban, Clock, ShieldOff, ArrowRight, Pencil, Trash2, RotateCcw, MailX, KeyRound, History, PlugZap } from "lucide-react";
import { getUsers, getRoles, assignRole, blockUser, unblockUser, updateUser, deleteUser, restoreUser, resendInvitation, cancelInvitation, reactivateInvitation, TenantUser, TenantRole } from "@/lib/api/security";
import { EditUserModal } from "./users/EditUserModal";
import { DeleteUserDialog } from "./users/DeleteUserDialog";
import { RestoreUserDialog } from "./users/RestoreUserDialog";
import { ResendInvitationButton } from "./users/ResendInvitationButton";
import { ReactivateInvitationButton } from "./users/ReactivateInvitationButton";
import { CancelInvitationDialog } from "./users/CancelInvitationDialog";
import { InviteUserModal } from "./users/InviteUserModal";
import { UsersTable, toUserRow } from "./users/UsersTable";
import { UserAuditHistoryDrawer } from "./users/UserAuditHistoryDrawer";
import { ResetPasswordDialog } from "./users/ResetPasswordDialog";
import { ModuleTitle } from "./ModuleTitle";
import { COPY } from "@/lib/copy/copy-catalog";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import { usePermissions } from "@/hooks/usePermissions";
import { ICT_CLIENTS_MANAGE_PERMISSION } from "@/lib/auth/jwt";
import { IctClientsPanel } from "./users/IctClientsPanel";
import { CompanyRolesPanel } from "./users/CompanyRolesPanel";
import { ExternalClientsPanel } from "./users/ExternalClientsPanel";
import {
  SuspendOrDeactivateModal,
  type SuspendMode,
} from "./users/SuspendOrDeactivateModal";
import { resolveProfile, targetEntityTypeForProfile } from "@/lib/users/profiles";
import { isInvitationRow } from "@/lib/users/invitationRow";
import { superadminClient } from "@/lib/api/superadmin-client";

import { formatFechaHora } from "@/lib/format/date";
// HU #10623 (AC3/AC4): "Eliminados" solo se ofrece a SuperAdmin. AdminCompany puede

// suspender/desactivar pero NO eliminar; el botón Eliminar y la pestaña Eliminados son SuperAdmin-only.
const ALL_TABS = [
  { id: "usuarios", label: "Usuarios", icon: Users },
  { id: "roles", label: "Roles y permisos", icon: Shield },
  { id: "clientes-ict", label: "Clientes ICT", icon: KeyRound },
  // HU #13200 — clientes de integración EXTERNOS (Flito), solo SuperAdmin.
  { id: "clientes-integracion", label: "Clientes de integración", icon: PlugZap },
  { id: "eliminados", label: "Eliminados", icon: Trash2 },
] as const;

type TabId = (typeof ALL_TABS)[number]["id"];

// Los chips de estado y la columna Perfil / Rol viven ahora en UsersTable, compartida por el
// módulo Usuarios, la ficha de compañía y el hub OT.

// Ajuste QA: la columna "Fecha" mostraba el ISO crudo (con microsegundos) de invitaciones
// pendientes y de "Eliminado el" en vez de una fecha legible.
function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return formatFechaHora(parsed);
}

export function Usuarios() {
  const { isSuperAdmin, isAdminCompany, userId: currentUserId, permissions, tenantId } = usePermissions();
  // Clientes ICT: SuperAdmin (bypass por rol) o quien tenga el permiso ict.clients.manage.
  const canManageIctClients = isSuperAdmin || permissions.includes(ICT_CLIENTS_MANAGE_PERMISSION);
  // Reset admin: SuperAdmin o AdminCompany (API acota al tenant).
  const canResetPassword = isSuperAdmin || isAdminCompany;
  // Suspender / desactivar / desbloquear: paridad AdminCompany en su empresa (API scoped).
  // Eliminar: SuperAdmin-only (AdminCompany ya no tiene este permiso).
  // Ver eliminados / restaurar siguen exclusivos de SuperAdmin.
  const canManageUserLifecycle = isSuperAdmin || isAdminCompany;
  const [open, setOpen] = useState(false);
  const pgEliminados = usePaginacion();
  const [tab, setTab] = useState<TabId>("usuarios");
  const [users, setUsers] = useState<TenantUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [roles, setRoles] = useState<TenantRole[]>([]);
  const [rolesLoading, setRolesLoading] = useState(true);
  const [suspendTarget, setSuspendTarget] = useState<{ user: TenantUser; mode: SuspendMode } | null>(null);
  const [editTarget, setEditTarget] = useState<TenantUser | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<TenantUser | null>(null);
  const [resetPasswordTarget, setResetPasswordTarget] = useState<TenantUser | null>(null);
  // HU #10624 — pestaña "Eliminados": usuarios de CUALQUIER tenant con deletedAt != null.
  const [deletedUsers, setDeletedUsers] = useState<TenantUser[]>([]);
  const [deletedLoading, setDeletedLoading] = useState(false);
  const [deletedError, setDeletedError] = useState<string | null>(null);
  const [restoreTarget, setRestoreTarget] = useState<TenantUser | null>(null);
  // HU #10628 — objetivo del diálogo de confirmación "Cancelar invitación" (filas "Pendiente").
  const [cancelTarget, setCancelTarget] = useState<TenantUser | null>(null);
  const [auditTarget, setAuditTarget] = useState<TenantUser | null>(null);
  const [editRoles, setEditRoles] = useState<TenantRole[]>([]);
  const [editRolesLoading, setEditRolesLoading] = useState(false);

  // AC4 (HU #10623): "Eliminados" es exclusivo de SuperAdmin. "Clientes ICT" requiere ict.clients.manage.
  // HU #13200 AC6: "Clientes de integración" es exclusivo de SuperAdmin (la API de la HU #13088 también).
  const tabs = ALL_TABS.filter(
    (t) =>
      (t.id !== "eliminados" || isSuperAdmin) &&
      // HU #13443: la gestión de roles es de SuperAdmin (atajo a RBAC) y de Admin de Compañía (roles propios).
      // ot_admin y el resto de roles no ven la pestaña: asignan roles desde la edición del usuario.
      (t.id !== "roles" || isSuperAdmin || isAdminCompany) &&
      (t.id !== "clientes-ict" || canManageIctClients) &&
      (t.id !== "clientes-integracion" || isSuperAdmin),
  );

  async function loadUsers() {
    setLoading(true);
    setError(null);
    try {
      const data = await getUsers();
      setUsers(data);
    } catch {
      setError("Error al cargar usuarios.");
    } finally {
      setLoading(false);
    }
  }

  // HU #10624 (AC3) — GET /api/v1/security/users?onlyDeleted=true, EXCLUSIVO de SuperAdmin.
  async function loadDeletedUsers() {
    setDeletedLoading(true);
    setDeletedError(null);
    try {
      const data = await getUsers(true);
      setDeletedUsers(data);
    } catch {
      setDeletedError("Error al cargar usuarios eliminados.");
    } finally {
      setDeletedLoading(false);
    }
  }

  async function loadRoles() {
    setRolesLoading(true);
    try {
      const data = await getRoles();
      setRoles(data);
    } catch {
      // silencioso — roles son opcionales para el dropdown
    } finally {
      setRolesLoading(false);
    }
  }

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    loadUsers();
    // AC4 (HU #10509): GET /api/v1/security/roles resuelve el tipo de entidad por el tenant
    // del caller — para el SuperAdmin (tenant interno) no aporta información útil, y el CRUD
    // de roles ya vive exclusivamente en RbacAdmin (HU #10508). Se evita la llamada y se
    // muestra en su lugar un atajo al módulo RBAC (ver tab "roles" más abajo).
    if (!isSuperAdmin) {
      loadRoles();
    } else {
      setRolesLoading(false);
    }
  }, [isSuperAdmin]);

  useEffect(() => {
    // HU #10624 — carga perezosa: solo al entrar a la pestaña "Eliminados" (SuperAdmin).
    if (tab === "eliminados" && isSuperAdmin) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      loadDeletedUsers();
    }
  }, [tab, isSuperAdmin]);

  // Roles para EditUserModal: AdminCompany usa getRoles(); SuperAdmin carga catálogo
  // COMPANY/OT según perfil del usuario objetivo (nunca FLIT/SuperAdmin).
  useEffect(() => {
    if (!editTarget || isInvitationRow(editTarget)) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setEditRoles([]);
      setEditRolesLoading(false);
      return;
    }
    const profile = resolveProfile(editTarget);
    if (profile === "FLIT") {
      setEditRoles([]);
      setEditRolesLoading(false);
      return;
    }
    if (!isSuperAdmin) {
      setEditRoles(roles);
      setEditRolesLoading(rolesLoading);
      return;
    }
    let cancelled = false;
    setEditRolesLoading(true);
    void superadminClient
      .listRoles(targetEntityTypeForProfile(profile))
      .then((list) => {
        if (cancelled) return;
        setEditRoles(
          list
            .filter((r) => r.isActive)
            .map((r) => ({
              id: r.id,
              code: r.code,
              name: r.name,
              description: r.description,
              isSystem: r.isSystem,
              permissionCount: r.permissionCount,
              createdAt: r.createdAt,
            })),
        );
      })
      .catch(() => {
        if (!cancelled) setEditRoles([]);
      })
      .finally(() => {
        if (!cancelled) setEditRolesLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [editTarget, isSuperAdmin, roles, rolesLoading]);

  // Acciones de cada fila. Se declaran como datos (RowAction[]) para que UsersTable las
  // renderice con el área de clic de 40x40 de RowActions — el tamaño anterior (28px) quedaba
  // por debajo del desfase del cursor SVG de FLIT y muchos clics caían fuera del botón.
  function actionsForUser(userId: string): RowAction[] {
    const u = users.find((x) => x.id === userId);
    if (!u) return [];

    const actions: RowAction[] = [];

    if (!isInvitationRow(u) && isSuperAdmin) {
      actions.push({
        icon: History,
        label: `Ver historial de ${u.fullName}`,
        tone: "primary",
        onClick: () => setAuditTarget(u),
      });
    }

    // Editar información (nombre/correo/rol): SuperAdmin, AdminCompany y ot_admin.
    // AC4 (HU #10622): sin botón para usuarios pendientes ni cancelados (HU #11552 / ADR-0048)
    // — el `id` de esas filas es un invitationId, no hay cuenta real que editar.
    if (!isInvitationRow(u)) {
      actions.push({
        icon: Pencil,
        label: `Editar usuario ${u.fullName}`,
        tone: "primary",
        onClick: () => setEditTarget(u),
      });
    }

    // Restablecer contraseña: SuperAdmin o AdminCompany (HU-B auth-parity; el API acota al tenant).
    if (!isInvitationRow(u) && canResetPassword) {
      actions.push({
        icon: KeyRound,
        label: `Restablecer contraseña de ${u.fullName}`,
        onClick: () => setResetPasswordTarget(u),
      });
    }

    // Bloquear/desactivar/reactivar: SuperAdmin (cualquier tenant) o AdminCompany (solo su
    // empresa — el API rechaza fuera de ámbito).
    if (!isInvitationRow(u) && canManageUserLifecycle) {
      if (u.isSuspended) {
        actions.push({
          icon: ShieldOff,
          label: `Desbloquear usuario ${u.fullName}`,
          onClick: () => void handleUnsuspend(u.id),
        });
      } else {
        actions.push({
          icon: Clock,
          label: `Suspender temporalmente a ${u.fullName}`,
          tone: "danger",
          onClick: () => setSuspendTarget({ user: u, mode: "temporary" }),
        });
        actions.push({
          icon: Ban,
          label: `Desactivar indefinidamente a ${u.fullName}`,
          tone: "primary",
          onClick: () => setSuspendTarget({ user: u, mode: "indefinite" }),
        });
      }
    }

    // Eliminar: exclusivo de SuperAdmin; nunca sobre la propia fila.
    // AdminCompany puede suspender/desactivar (canManageUserLifecycle) pero NO eliminar.
    // Restaurar sigue siendo exclusivo de SuperAdmin (pestaña "Eliminados").
    if (!isInvitationRow(u) && isSuperAdmin && u.id !== currentUserId) {
      actions.push({
        icon: Trash2,
        label: `Eliminar usuario ${u.fullName}`,
        tone: "danger",
        onClick: () => setDeleteTarget(u),
      });
    }

    // AC2 (HU #10628): "Cancelar invitación" SOLO en filas pendientes (una cancelada ya no
    // tiene nada que cancelar — su acción es "Reactivar", ver extraActionsFor de UsersTable).
    if (u.status === "pending") {
      actions.push({
        icon: MailX,
        label: `Cancelar invitación a ${u.fullName}`,
        tone: "danger",
        onClick: () => setCancelTarget(u),
      });
    }

    return actions;
  }

  function handleInviteSuccess() {
    loadUsers();
  }

  async function handleSuspend(userId: string, reason: string, endsAt: string | null) {
    await blockUser(userId, reason, endsAt);
    loadUsers();
  }

  async function handleUnsuspend(userId: string) {
    await unblockUser(userId);
    loadUsers();
  }

  // HU #10623 — AC1: la confirmación (con el aviso de que solo un SuperAdmin puede restaurar)
  // vive en DeleteUserDialog; aquí solo se persiste. Errores 400/409 los mapea el propio diálogo.
  async function handleDelete(userId: string, rowVersion: number) {
    return deleteUser(userId, rowVersion);
  }

  // HU #10624 (AC3) — la confirmación vive en RestoreUserDialog; aquí solo se persiste.
  async function handleRestore(userId: string) {
    return restoreUser(userId);
  }

  // HU #10628 — la confirmación (distinta de "Eliminar usuario", AC2) vive en
  // CancelInvitationDialog; aquí solo se persiste. Errores 404/409 (AC3) los mapea el propio diálogo.
  async function handleCancelInvitation(invitationId: string) {
    return cancelInvitation(invitationId);
  }

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <ModuleTitle
        title={COPY.A23}
        subtitle="Gestiona el acceso de tu equipo a la plataforma."
        action={
          // El backend exige AdminCompanyPolicy para invitar (Bug #11581). El acceso a este
          // modulo es por permiso RBAC, no por rol, asi que un rol personalizado con el modulo
          // concedido veria el boton y recibiria un 403 opaco: se gatea con el mismo criterio
          // que el resto de acciones de ciclo de vida.
          tab === "usuarios" && canManageUserLifecycle ? (
            <button onClick={() => setOpen(true)} className="flex shrink-0 items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white" style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}>
              Invitar usuario
            </button>
          ) : undefined
        }
      />

      <div className="flex flex-wrap items-center gap-1 border-b border-[#DFE5ED] dark:border-white/10 shrink-0">
        {tabs.map((t) => {
          const Icon = t.icon;
          const active = tab === t.id;
          return (
            <button
              key={t.id}
              onClick={() => setTab(t.id)}
              className={`relative flex items-center gap-2 px-4 py-2.5 text-xs font-semibold transition ${
                active ? "text-[color:var(--flit-brand-ink)] opacity-100" : "opacity-65"
              }`}
            >
              <Icon className="h-3.5 w-3.5" aria-hidden="true" />
              {t.label}
              {active && (
                <span
                  className="absolute bottom-0 left-0 right-0 h-0.5 rounded-full bg-[color:var(--color-flit-brand)]"
                  aria-hidden="true"
                />
              )}
            </button>
          );
        })}
      </div>

      {tab === "usuarios" && (
        <UsersTable
          rows={users.map((u) => toUserRow(u))}
          loading={loading}
          error={error}
          onRetry={loadUsers}
          showTenantColumn={isSuperAdmin}
          emptyMessage={
            isSuperAdmin
              ? "No hay usuarios en ninguna compañía."
              : "No hay usuarios en este tenant. Invita al primero."
          }
          actionsFor={(row) => actionsForUser(row.id)}
          extraActionsFor={(row) => {
            // AC3 (HU #10626): SOLO en filas "Pendiente" — el id de la fila ya es el
            // invitationId. Vive fuera de RowActions porque lleva cooldown y mensaje inline.
            if (row.status === "pending") {
              return (
                <ResendInvitationButton
                  invitationId={row.id}
                  fullName={row.fullName}
                  resend={resendInvitation}
                />
              );
            }
            // HU #11552 / ADR-0048: SOLO en filas "Cancelada" — el id de la fila ya es el
            // invitationId. Tras reactivar, la fila vuelve a verse como "Pendiente" recargando
            // el listado, sin recargar la página.
            if (row.status === "cancelled") {
              return (
                <ReactivateInvitationButton
                  invitationId={row.id}
                  fullName={row.fullName}
                  reactivate={reactivateInvitation}
                  onReactivated={() => loadUsers()}
                />
              );
            }
            return null;
          }}
        />
      )}

      {tab === "clientes-ict" && canManageIctClients && (
        // Clientes de integración ICT (ronda 2, Feature #10888): CRUD de las credenciales que usan los
        // gestores para registrar pre-trámites. SuperAdmin administra cualquier compañía; el resto la suya.
        <IctClientsPanel isSuperAdmin={isSuperAdmin} tenantId={tenantId} />
      )}

      {tab === "clientes-integracion" && isSuperAdmin && (
        // HU #13200 (Épica #12737): clientes de integración externos (Flito) que leen el feed de trámites.
        <ExternalClientsPanel />
      )}

      {tab === "eliminados" && isSuperAdmin && (
        // HU #10624 (AC3) — GET /api/v1/security/users?onlyDeleted=true: usuarios eliminados
        // (soft-delete) de CUALQUIER tenant, exclusivo de SuperAdmin. Restaurar (1 clic de
        // confirmación en RestoreUserDialog) deshace el soft-delete vía restoreUser().
        // Bug #13055 — tabla homologada con el modelo de trámites: `<table>` semántica, RowActions,
        // loader del carrito y paginación en cliente con «Filas por página».
        <div className="flex flex-col gap-3">
          {deletedLoading && <CarLoaderModal label="Cargando usuarios eliminados…" />}
          {!deletedLoading && deletedError && (
            <div role="alert" className="py-12 text-center text-sm" style={{ color: "#FF4E00" }}>{deletedError}</div>
          )}
          {!deletedLoading && !deletedError && deletedUsers.length === 0 && (
            <div className="py-12 text-center text-sm opacity-60">
              No hay usuarios eliminados de ninguna compañía u organismo.
            </div>
          )}
          {!deletedLoading && !deletedError && deletedUsers.length > 0 && (
            <>
              <div className="overflow-x-auto">
                <table
                  aria-label="Usuarios eliminados"
                  style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px", minWidth: 560 }}
                  className="text-xs"
                >
                  <thead>
                    <tr>
                      {["Usuario", "Empresa", "Eliminado el", "Acciones"].map((col, i, arr) => (
                        <th
                          key={col}
                          scope="col"
                          className={`${TABLA_HEADER_CELL_CLS} ${i === 0 ? "rounded-l-xl" : ""} ${i === arr.length - 1 ? "rounded-r-xl text-right" : ""}`}
                          style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                        >
                          {col}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {pgEliminados.paginar(deletedUsers).map((u) => (
                      <tr
                        // Mismo criterio que la tabla de "Usuarios": u.id + u.roleId evita colisión de
                        // key cuando el JOIN produce N filas por usuario con N roles.
                        key={`${u.id}-${u.roleId ?? "sin-rol"}`}
                        className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}
                      >
                        <td className="rounded-l-xl border-y border-l px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                          <p className="font-semibold">{u.fullName}</p>
                          <p className="text-[10px] opacity-60">{u.email}</p>
                        </td>
                        <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                          <span className="block truncate opacity-70">{u.tenantName ?? "—"}</span>
                        </td>
                        <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                          <span className="opacity-70">{formatDateTime(u.deletedAt)}</span>
                        </td>
                        <td className="rounded-r-xl border-y border-r px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                          <RowActions
                            actions={[
                              {
                                icon: RotateCcw,
                                label: `Restaurar usuario ${u.fullName}`,
                                tone: "primary",
                                onClick: () => setRestoreTarget(u),
                              },
                            ]}
                          />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <Pagination
                page={pgEliminados.page}
                pageSize={pgEliminados.pageSize}
                totalCount={deletedUsers.length}
                onPageChange={pgEliminados.setPage}
                onPageSizeChange={pgEliminados.setPageSize}
                noun="usuarios eliminados"
                ariaLabel="Paginación de usuarios eliminados"
              />
            </>
          )}
        </div>
      )}

      {tab === "roles" && isSuperAdmin && (
        // AC4 (HU #10509) — el CRUD completo de roles es exclusivo de RbacAdmin (HU #10508);
        // no se duplica aquí. El SuperAdmin solo recibe un atajo directo al módulo RBAC.
        <div className="flex flex-col items-center gap-3 py-14 text-center">
          <div className="h-12 w-12 rounded-full grid place-items-center" style={{ background: "rgba(85,126,255,0.10)" }}>
            <Shield className="h-5 w-5" style={{ color: "#557EFF" }} aria-hidden="true" />
          </div>
          <div>
            <p className="text-sm font-semibold">La gestión de roles del sistema vive en el módulo RBAC</p>
            <p className="text-xs opacity-60 mt-0.5">
              Crea, edita, activa/desactiva o elimina roles de Compañía y de Organismo de Tránsito.
            </p>
          </div>
          <Link
            href="/?m=rbac"
            className="flex items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            Ir a Roles y permisos (RBAC)
            <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
          </Link>
        </div>
      )}

      {tab === "roles" && !isSuperAdmin && isAdminCompany && <CompanyRolesPanel />}

      {open && (
        <InviteUserModal
          onClose={() => setOpen(false)}
          onSuccess={handleInviteSuccess}
          roles={roles}
          rolesLoading={rolesLoading}
          isSuperAdmin={isSuperAdmin}
        />
      )}
      {suspendTarget && (
        <SuspendOrDeactivateModal
          user={suspendTarget.user}
          mode={suspendTarget.mode}
          onClose={() => setSuspendTarget(null)}
          onConfirm={async (reason, endsAt) => {
            await handleSuspend(suspendTarget.user.id, reason, endsAt);
            setSuspendTarget(null);
          }}
        />
      )}
      {editTarget && (
        <EditUserModal
          user={{
            id: editTarget.id,
            fullName: editTarget.fullName,
            email: editTarget.email,
            rowVersion: editTarget.rowVersion,
          }}
          onClose={() => setEditTarget(null)}
          onSaved={() => {
            setEditTarget(null);
            loadUsers();
          }}
          onUpdate={updateUser}
          profile={resolveProfile(editTarget)}
          roleSection={
            resolveProfile(editTarget) === "FLIT"
              ? undefined
              : {
                  currentRoleName: editTarget.role,
                  currentRoleId: editTarget.roleId,
                  roles: editRoles,
                  rolesLoading: editRolesLoading,
                  onAssignRole: async (roleId) => {
                    await assignRole(editTarget.id, roleId);
                    await loadUsers();
                    setEditTarget((prev) => {
                      if (!prev || prev.id !== editTarget.id) return prev;
                      const match = editRoles.find((r) => r.id === roleId);
                      return {
                        ...prev,
                        roleId,
                        role: match?.name ?? prev.role,
                        roleCode: match?.code ?? prev.roleCode,
                      };
                    });
                  },
                }
          }
        />
      )}
      {auditTarget && isSuperAdmin && (
        <UserAuditHistoryDrawer
          userId={auditTarget.id}
          userLabel={auditTarget.fullName}
          onClose={() => setAuditTarget(null)}
        />
      )}
      {resetPasswordTarget && (
        <ResetPasswordDialog
          user={{
            fullName: resetPasswordTarget.fullName,
            email: resetPasswordTarget.email,
          }}
          onClose={() => setResetPasswordTarget(null)}
          onDone={() => {
            /* El listado no cambia; el usuario solo debe re-autenticarse con la temporal. */
          }}
        />
      )}
      {deleteTarget && (
        <DeleteUserDialog
          user={{
            id: deleteTarget.id,
            fullName: deleteTarget.fullName,
            email: deleteTarget.email,
            rowVersion: deleteTarget.rowVersion,
          }}
          onClose={() => setDeleteTarget(null)}
          onDeleted={() => {
            setDeleteTarget(null);
            loadUsers();
          }}
          onDelete={handleDelete}
        />
      )}
      {cancelTarget && (
        <CancelInvitationDialog
          invitation={{
            id: cancelTarget.id,
            fullName: cancelTarget.fullName,
            email: cancelTarget.email,
          }}
          onClose={() => setCancelTarget(null)}
          onCancelled={() => {
            setCancelTarget(null);
            loadUsers();
          }}
          onStale={loadUsers}
          onCancel={handleCancelInvitation}
        />
      )}
      {restoreTarget && (
        <RestoreUserDialog
          user={{
            id: restoreTarget.id,
            fullName: restoreTarget.fullName,
            email: restoreTarget.email,
          }}
          onClose={() => setRestoreTarget(null)}
          onRestored={() => {
            setRestoreTarget(null);
            loadDeletedUsers();
            // Ajuste QA: sin este refresco, la pestaña "Usuarios" quedaba con el estado
            // viejo (sin el usuario restaurado) hasta recargar la página manualmente.
            loadUsers();
          }}
          onRestore={handleRestore}
        />
      )}
    </div>
  );
}


