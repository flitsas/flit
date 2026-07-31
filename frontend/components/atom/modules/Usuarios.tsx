"use client";

import { useState, useEffect } from "react";
import Link from "next/link";
import { Search, X, Users, Shield, Ban, Clock, ShieldOff, Landmark, ArrowRight, Pencil, Trash2, RotateCcw, MailX } from "lucide-react";
import { createInvitation, getUsers, getRoles, assignRole, blockUser, unblockUser, updateUser, deleteUser, restoreUser, resendInvitation, cancelInvitation, TenantUser, TenantRole } from "@/lib/api/security";
import { ApiError } from "@/lib/api/types";
import { EditUserModal } from "./users/EditUserModal";
import { DeleteUserDialog } from "./users/DeleteUserDialog";
import { RestoreUserDialog } from "./users/RestoreUserDialog";
import { ResendInvitationButton } from "./users/ResendInvitationButton";
import { CancelInvitationDialog } from "./users/CancelInvitationDialog";
import { ModuleTitle } from "./ModuleTitle";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import { fetchTransitOfficeTenants, type TransitOfficeTenantItem } from "@/lib/api/admin-transit-office-tenants";
import type { CompanyListItem } from "@/lib/api/types";
import { usePermissions } from "@/hooks/usePermissions";
import {
  SuspendOrDeactivateModal,
  type SuspendMode,
} from "./users/SuspendOrDeactivateModal";

// HU #10623 (AC3/AC4): "Eliminados" solo se ofrece a SuperAdmin — AdminCompany/OtAdmin ven
// "Eliminar" (AC1) pero nunca la vista de restauración, exclusiva de SuperAdmin.
const ALL_TABS = [
  { id: "usuarios", label: "Usuarios", icon: Users },
  { id: "roles", label: "Roles y permisos", icon: Shield },
  { id: "eliminados", label: "Eliminados", icon: Trash2 },
] as const;

type TabId = (typeof ALL_TABS)[number]["id"];

// Chips tintados (HU #10494 · decisión D1, tones HU #10844). Mismo vocabulario
// (Activo/Inactivo/Pendiente); el color lo resuelve el `tone` semántico desde globals.css.
const STATUS_BADGE: Record<
  TenantUser["status"],
  { label: string; tone: StatusTone }
> = {
  active: { label: "Activo", tone: "success" },
  inactive: { label: "Inactivo", tone: "danger" },
  pending: { label: "Pendiente", tone: "warning" },
};

// Ajuste QA (flujo completo HU #10619-#10628): un usuario suspendido/desactivado seguía
// mostrando el chip "Activo" — solo cambiaba el ícono de acción (Ban → ShieldOff), sin
// ninguna señal visible al escanear la tabla. Prevalece sobre STATUS_BADGE[status].
const SUSPENDED_BADGE: { label: string; tone: StatusTone } = { label: "Bloqueado", tone: "danger" };

function userBadge(u: TenantUser) {
  return u.isSuspended ? SUSPENDED_BADGE : STATUS_BADGE[u.status];
}

// Ajuste QA: la columna "Fecha" mostraba el ISO crudo (con microsegundos) de invitaciones
// pendientes y de "Eliminado el" en vez de una fecha legible.
function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short" }).format(parsed);
}

export function Usuarios() {
  const { isSuperAdmin, userId: currentUserId } = usePermissions();
  const [open, setOpen] = useState(false);
  const [tab, setTab] = useState<TabId>("usuarios");
  const [users, setUsers] = useState<TenantUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [roles, setRoles] = useState<TenantRole[]>([]);
  const [rolesLoading, setRolesLoading] = useState(true);
  const [suspendTarget, setSuspendTarget] = useState<{ user: TenantUser; mode: SuspendMode } | null>(null);
  const [editTarget, setEditTarget] = useState<TenantUser | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<TenantUser | null>(null);
  // HU #10624 — pestaña "Eliminados": usuarios de CUALQUIER tenant con deletedAt != null.
  const [deletedUsers, setDeletedUsers] = useState<TenantUser[]>([]);
  const [deletedLoading, setDeletedLoading] = useState(false);
  const [deletedError, setDeletedError] = useState<string | null>(null);
  const [restoreTarget, setRestoreTarget] = useState<TenantUser | null>(null);
  // HU #10628 — objetivo del diálogo de confirmación "Cancelar invitación" (filas "Pendiente").
  const [cancelTarget, setCancelTarget] = useState<TenantUser | null>(null);

  // AC4 (HU #10623): "Eliminados" es exclusivo de SuperAdmin.
  const tabs = isSuperAdmin ? ALL_TABS : ALL_TABS.filter((t) => t.id !== "eliminados");

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
        title="Administración de usuarios y permisos"
        subtitle="Gestiona el acceso de tu equipo a la plataforma."
        action={
          tab === "usuarios" ? (
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
              className="relative flex items-center gap-2 px-4 py-2.5 text-xs font-semibold transition"
              style={{ color: active ? "#557EFF" : undefined, opacity: active ? 1 : 0.65 }}
            >
              <Icon className="h-3.5 w-3.5" />
              {t.label}
              {active && <span className="absolute bottom-0 left-0 right-0 h-0.5 rounded-full" style={{ background: "#557EFF" }} />}
            </button>
          );
        })}
        {tab === "usuarios" && (
          <div className="ml-auto mb-1.5 flex w-full max-w-xs shrink-0 items-center gap-2 rounded-xl border bg-white px-3 py-1.5 dark:bg-[#0B0F14]">
            <Search className="h-4 w-4 opacity-60" />
            <input placeholder="Buscar por nombre o correo..." className="flex-1 bg-transparent outline-none text-xs" />
          </div>
        )}
      </div>

      {tab === "usuarios" && (
        <>
          <div className="flex flex-col overflow-x-auto">
            <div
              className="grid min-w-[720px] px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl shrink-0"
              style={{
                gridTemplateColumns: isSuperAdmin ? "3fr 2fr 2fr 1.5fr 1.5fr 40px" : "4fr 2fr 2fr 3fr 40px",
                background: "#DFE5ED",
                color: "#162744",
              }}
            >
              <div>Usuario</div>
              {isSuperAdmin && <div>Empresa</div>}
              <div>Rol</div>
              <div>Estado</div>
              <div>Fecha</div>
              <div />
            </div>

            <div className="space-y-2 pt-2">
              {loading && (
                <div className="py-12 text-center text-sm opacity-60">Cargando usuarios…</div>
              )}
              {!loading && error && (
                <div role="alert" className="py-12 text-center text-sm" style={{ color: "#FF4E00" }}>{error}</div>
              )}
              {!loading && !error && users.length === 0 && (
                <div className="py-12 text-center text-sm opacity-60">
                  {isSuperAdmin ? "No hay usuarios en ninguna compañía." : "No hay usuarios en este tenant. Invita al primero."}
                </div>
              )}
              {!loading && !error && users.map((u) => {
                const badge = userBadge(u);
                return (
                  <div
                    // GET /api/v1/security/users hace JOIN vía UserRoleAssignments: un usuario
                    // con N roles activos produce N filas con el mismo u.id (una por asignación
                    // de rol). u.id solo no es único — se compone con u.roleId para evitar el
                    // warning de React "two children with the same key".
                    key={`${u.id}-${u.roleId ?? "sin-rol"}`}
                    className="grid min-w-[720px] items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs"
                    style={{
                      gridTemplateColumns: isSuperAdmin ? "3fr 2fr 2fr 1.5fr 1.5fr 40px" : "4fr 2fr 2fr 3fr 40px",
                      }}
                  >
                    <div>
                      <p className="font-semibold">{u.fullName}</p>
                      <p className="text-[10px] opacity-60">{u.email}</p>
                    </div>
                    {isSuperAdmin && (
                      <div className="opacity-70 truncate">{u.tenantName ?? "—"}</div>
                    )}
                    <div>
                      {u.status !== "pending" && !isSuperAdmin ? (
                        <RoleDropdown
                          userId={u.id}
                          currentRoleName={u.role}
                          roles={roles}
                          rolesLoading={rolesLoading}
                          onAssigned={loadUsers}
                        />
                      ) : (
                        <span className="opacity-70">{u.role ?? "—"}</span>
                      )}
                    </div>
                    <div>
                      <StatusBadge label={badge.label} tone={badge.tone} />
                    </div>
                    <div className="opacity-70">{formatDateTime(u.createdAt)}</div>
                    <div className="flex items-center justify-end gap-1">
                      {/* Editar información (nombre/correo): disponible para SuperAdmin,
                          AdminCompany y ot_admin. AC4 (HU #10622): sin botón para usuarios
                          pendientes — todavía no hay una cuenta real que editar. */}
                      {u.status !== "pending" && (
                        <button
                          title="Editar usuario"
                          aria-label={`Editar usuario ${u.fullName}`}
                          onClick={() => setEditTarget(u)}
                          className="p-1.5 rounded-lg transition hover:bg-blue-50"
                          style={{ color: "#557EFF" }}
                        >
                          <Pencil className="h-4 w-4" />
                        </button>
                      )}
                      {/* Bloquear/desactivar/reactivar es EXCLUSIVO de SuperAdmin.
                          AdminCompany y ot_admin no pueden suspender ni reactivar. */}
                      {u.status !== "pending" && isSuperAdmin && (
                        u.isSuspended ? (
                          <button
                            title="Desbloquear usuario"
                            aria-label={`Desbloquear usuario ${u.fullName}`}
                            onClick={() => handleUnsuspend(u.id)}
                            className="p-1.5 rounded-lg transition hover:bg-green-50"
                            style={{ color: "#00DBD5" }}
                          >
                            <ShieldOff className="h-4 w-4" />
                          </button>
                        ) : (
                          <>
                            <button
                              title="Suspender temporalmente"
                              aria-label={`Suspender temporalmente a ${u.fullName}`}
                              onClick={() => setSuspendTarget({ user: u, mode: "temporary" })}
                              className="p-1.5 rounded-lg transition hover:bg-orange-50"
                              style={{ color: "#FF4E00" }}
                            >
                              <Clock className="h-4 w-4" />
                            </button>
                            <button
                              title="Desactivar indefinidamente"
                              aria-label={`Desactivar indefinidamente a ${u.fullName}`}
                              onClick={() => setSuspendTarget({ user: u, mode: "indefinite" })}
                              className="p-1.5 rounded-lg transition hover:bg-red-50"
                              style={{ color: "#557EFF" }}
                            >
                              <Ban className="h-4 w-4" />
                            </button>
                          </>
                        )
                      )}
                      {/* Eliminar es EXCLUSIVO de SuperAdmin (AdminCompany/ot_admin no pueden).
                          AC2 (HU #10623): sin botón "Eliminar" sobre la propia fila —
                          nunca puede auto-eliminarse. */}
                      {u.status !== "pending" && isSuperAdmin && u.id !== currentUserId && (
                        <button
                          title="Eliminar usuario"
                          aria-label={`Eliminar usuario ${u.fullName}`}
                          onClick={() => setDeleteTarget(u)}
                          className="p-1.5 rounded-lg transition hover:bg-red-50"
                          style={{ color: "#FF4E00" }}
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      )}
                      {/* AC3 (HU #10626): SOLO en filas "Pendiente" — el id de la fila ya es el
                          invitationId. Gestión de invitaciones disponible para todos los
                          administradores (parte del flujo de invitar). */}
                      {u.status === "pending" && (
                        <ResendInvitationButton
                          invitationId={u.id}
                          fullName={u.fullName}
                          resend={resendInvitation}
                        />
                      )}
                      {/* AC2 (HU #10628): "Cancelar invitación" SOLO en filas "Pendiente" —
                          mutuamente excluyente con "Eliminar usuario" (arriba, solo status !== "pending").
                          Disponible para todos los administradores (gestión de invitaciones). */}
                      {u.status === "pending" && (
                        <button
                          type="button"
                          title="Cancelar invitación"
                          aria-label={`Cancelar invitación a ${u.fullName}`}
                          onClick={() => setCancelTarget(u)}
                          className="p-1.5 rounded-lg transition hover:bg-red-50"
                          style={{ color: "#FF4E00" }}
                        >
                          <MailX className="h-4 w-4" />
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
            {!loading && !error && users.length > 0 && (
              <p className="text-[10px] opacity-60 text-right pt-2 shrink-0">
                Mostrando {users.length} usuario{users.length !== 1 ? "s" : ""}
              </p>
            )}
          </div>
        </>
      )}

      {tab === "eliminados" && isSuperAdmin && (
        // HU #10624 (AC3) — GET /api/v1/security/users?onlyDeleted=true: usuarios eliminados
        // (soft-delete) de CUALQUIER tenant, exclusivo de SuperAdmin. Restaurar (1 clic de
        // confirmación en RestoreUserDialog) deshace el soft-delete vía restoreUser().
        <div className="flex flex-col overflow-x-auto">
          <div
            className="grid min-w-[560px] px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl shrink-0"
            style={{ gridTemplateColumns: "3fr 2fr 2fr 40px", background: "#DFE5ED", color: "#162744" }}
          >
            <div>Usuario</div>
            <div>Empresa</div>
            <div>Eliminado el</div>
            <div />
          </div>

          <div className="space-y-2 pt-2">
            {deletedLoading && (
              <div role="status" className="py-12 text-center text-sm opacity-60">Cargando usuarios eliminados…</div>
            )}
            {!deletedLoading && deletedError && (
              <div role="alert" className="py-12 text-center text-sm" style={{ color: "#FF4E00" }}>{deletedError}</div>
            )}
            {!deletedLoading && !deletedError && deletedUsers.length === 0 && (
              <div className="py-12 text-center text-sm opacity-60">
                No hay usuarios eliminados de ninguna compañía u organismo.
              </div>
            )}
            {!deletedLoading && !deletedError && deletedUsers.map((u) => (
              <div
                // Mismo criterio que la tabla de "Usuarios": u.id + u.roleId evita colisión de
                // key cuando el JOIN produce N filas por usuario con N roles.
                key={`${u.id}-${u.roleId ?? "sin-rol"}`}
                className="grid min-w-[560px] items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs"
                style={{ gridTemplateColumns: "3fr 2fr 2fr 40px" }}
              >
                <div>
                  <p className="font-semibold">{u.fullName}</p>
                  <p className="text-[10px] opacity-60">{u.email}</p>
                </div>
                <div className="opacity-70 truncate">{u.tenantName ?? "—"}</div>
                <div className="opacity-70">{formatDateTime(u.deletedAt)}</div>
                <div className="flex justify-end">
                  <button
                    title="Restaurar usuario"
                    aria-label={`Restaurar usuario ${u.fullName}`}
                    onClick={() => setRestoreTarget(u)}
                    className="p-1.5 rounded-lg transition hover:bg-blue-50"
                    style={{ color: "#557EFF" }}
                  >
                    <RotateCcw className="h-4 w-4" />
                  </button>
                </div>
              </div>
            ))}
          </div>
          {!deletedLoading && !deletedError && deletedUsers.length > 0 && (
            <p className="text-[10px] opacity-60 text-right pt-2 shrink-0">
              Mostrando {deletedUsers.length} usuario{deletedUsers.length !== 1 ? "s" : ""} eliminado{deletedUsers.length !== 1 ? "s" : ""}
            </p>
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

      {tab === "roles" && !isSuperAdmin && (
        <div className="flex flex-col gap-3">
          {/* AC4 (HU #10509) — modo SOLO LECTURA para AdminCompany/OtAdmin: sin botones de
              crear/editar/eliminar/desactivar. La gobernanza de roles es exclusiva de SuperAdmin. */}
          <p className="text-xs opacity-60">
            Roles disponibles para tu empresa. Solo el Super Admin puede crear, editar o desactivar roles.
          </p>
          {rolesLoading ? (
            <div className="py-12 text-center text-sm opacity-60">Cargando roles…</div>
          ) : roles.length === 0 ? (
            <div className="py-12 text-center text-sm opacity-60">
              No hay roles configurados para este tenant. Contacta al Super Admin.
            </div>
          ) : (
            <div className="flex flex-col overflow-x-auto">
              <div className="grid grid-cols-12 min-w-[560px] px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl shrink-0" style={{ background: "#DFE5ED", color: "#162744" }}>
                <div className="col-span-2">Código</div>
                <div className="col-span-4">Nombre</div>
                <div className="col-span-4">Descripción</div>
                <div className="col-span-2 text-center">Permisos</div>
              </div>
              <div className="space-y-2 pt-2">
                {roles.map((r) => (
                  <div key={r.id} className="grid grid-cols-12 min-w-[560px] items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs">
                    <div className="col-span-2 font-mono opacity-80">{r.code}</div>
                    <div className="col-span-4 font-semibold">{r.name}</div>
                    <div className="col-span-4 opacity-70">{r.description ?? "—"}</div>
                    <div className="col-span-2 text-center font-bold" style={{ color: "#557EFF" }}>{r.permissionCount}</div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {open && <InviteModal onClose={() => setOpen(false)} onSuccess={handleInviteSuccess} roles={roles} rolesLoading={rolesLoading} isSuperAdmin={isSuperAdmin} />}
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

function RoleDropdown({
  userId, currentRoleName, roles, rolesLoading, onAssigned,
}: {
  userId: string;
  currentRoleName: string | null;
  roles: TenantRole[];
  rolesLoading: boolean;
  onAssigned: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  async function handleChange(e: React.ChangeEvent<HTMLSelectElement>) {
    const roleId = e.target.value;
    if (!roleId) return;
    setBusy(true);
    setErr(null);
    try {
      await assignRole(userId, roleId);
      onAssigned();
    } catch {
      setErr("Error al asignar.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-0.5">
      <select
        aria-label="Asignar rol"
        value=""
        onChange={handleChange}
        disabled={busy || rolesLoading || roles.length === 0}
        className="text-[11px] rounded-lg border px-2 py-1 bg-transparent outline-none"
        style={{ minWidth: 100 }}
      >
        <option value="" disabled>
          {busy ? "Asignando…" : (currentRoleName ?? "Sin rol ▾")}
        </option>
        {roles.map((r) => (
          <option key={r.id} value={r.id}>{r.name}</option>
        ))}
      </select>
      {err && <span className="text-[10px]" style={{ color: "#FF4E00" }} role="alert">{err}</span>}
    </div>
  );
}

function InviteModal({
  onClose, onSuccess, roles, rolesLoading, isSuperAdmin,
}: {
  onClose: () => void;
  onSuccess: () => void;
  roles: TenantRole[];
  rolesLoading: boolean;
  isSuperAdmin: boolean;
}) {
  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  // HU #10510 AC1/AC3: selección MÚLTIPLE de roles (antes single-value opcional). Al menos
  // 1 rol es obligatorio para AdminCompany/OtAdmin — el SuperAdmin no usa este set (el rol de
  // sistema lo fuerza el backend según el tenant destino).
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>([]);
  const [selectedTenantId, setSelectedTenantId] = useState("");
  const [companies, setCompanies] = useState<{ id: string; name: string }[]>([]);
  const [transitOfficeTenants, setTransitOfficeTenants] = useState<TransitOfficeTenantItem[]>([]);
  const [tenantsLoading, setTenantsLoading] = useState(false);
  const [status, setStatus] = useState<"idle" | "loading" | "done" | "done_no_email">("idle");
  const [error, setError] = useState<string | null>(null);
  const [invitedEmail, setInvitedEmail] = useState("");

  // El rol de sistema a asignar ya no se limita a AdminCompany: el backend lo resuelve
  // según el tipo de tenant destino (ot_admin para organismos de tránsito).
  const isSelectedTenantOt = transitOfficeTenants.some((t) => t.id === selectedTenantId);

  useEffect(() => {
    if (!isSuperAdmin) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTenantsLoading(true);
    Promise.all([
      fetchCompaniesIndex({ pageSize: 200 }),
      fetchTransitOfficeTenants({ pageSize: 200 }),
    ])
      .then(([companiesResult, otResult]) => {
        setCompanies(companiesResult.data.map((c: CompanyListItem) => ({ id: c.id, name: c.razonSocial })));
        setTransitOfficeTenants(otResult.data);
      })
      .catch(() => { /* silencioso */ })
      .finally(() => setTenantsLoading(false));
  }, [isSuperAdmin]);

  const isDone = status === "done" || status === "done_no_email";

  function toggleRole(roleId: string) {
    setSelectedRoleIds((prev) =>
      prev.includes(roleId) ? prev.filter((id) => id !== roleId) : [...prev, roleId],
    );
  }

  // AC3 — al menos un rol es obligatorio para AdminCompany/OtAdmin (el SuperAdmin no
  // selecciona rol: el backend lo fuerza según el tenant destino).
  const rolesRequiredAndMissing = !isSuperAdmin && selectedRoleIds.length === 0;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (isSuperAdmin && !selectedTenantId) {
      setError("Debes seleccionar la empresa destino.");
      return;
    }

    if (rolesRequiredAndMissing) {
      setError("Selecciona al menos un rol para el usuario invitado.");
      return;
    }

    setStatus("loading");
    try {
      const result = await createInvitation(
        email.trim(),
        fullName.trim(),
        isSuperAdmin ? [] : selectedRoleIds,
        isSuperAdmin ? selectedTenantId : undefined,
      );
      setInvitedEmail(result.email);
      setStatus(result.emailSent ? "done" : "done_no_email");
      onSuccess();
    } catch (err) {
      const apiErr = err as ApiError;
      const s = apiErr.status;
      const code = (apiErr.body as { code?: string } | undefined)?.code;
      setError(
        code === "NO_ROLES_SELECTED"
          ? "Selecciona al menos un rol para el usuario invitado."
          : s === 409
            ? "Ya existe una invitación pendiente para este correo."
            : s === 404
              ? "El rol especificado no existe en el tenant."
              : s === 400
                ? "Debes seleccionar una empresa destino válida."
                : "No se pudo enviar la invitación. Inténtalo de nuevo."
      );
      setStatus("idle");
    }
  }

  return (
    <div className="fixed inset-0 z-50 grid place-items-center overflow-y-auto bg-black/40 backdrop-blur-sm px-4 py-6">
      <div className="bg-white dark:bg-[#0B0F14] rounded-2xl p-6 w-full max-w-md max-h-[90dvh] overflow-y-auto border">
        <div className="flex items-start justify-between mb-4">
          <div>
            <h3 className="text-lg font-bold">Invitar usuario</h3>
            <p className="text-xs opacity-70 mt-0.5">Asigna el acceso para colaborar dentro de FLIT.</p>
          </div>
          <button onClick={onClose} aria-label="Cerrar"><X className="h-5 w-5" /></button>
        </div>

        {isDone ? (
          <div className="space-y-3">
            <div className="rounded-xl p-3 border" style={{ borderColor: "#00DBD5", background: "rgba(0,219,213,0.06)" }}>
              <p className="text-sm font-semibold" style={{ color: "#00DBD5" }}>Invitación enviada</p>
              <p className="text-xs opacity-70 mt-0.5">Se enviaron instrucciones de activación a <strong>{invitedEmail}</strong>.</p>
              {status === "done_no_email" && (
                <p className="text-xs mt-1.5 font-medium" style={{ color: "#F9AC00" }}>
                  El correo no pudo entregarse. El administrador puede reintentar más tarde.
                </p>
              )}
            </div>
            <div className="rounded-xl p-3 border bg-[rgba(0,219,213,0.06)]">
              <p className="text-[10px] font-semibold uppercase opacity-60 mb-2">Onboarding</p>
              <div className="flex items-center gap-2 text-xs">
                {["Invitación enviada", "Activación", "Primer acceso"].map((step, i) => (
                  <span key={step} className="flex items-center gap-1">
                    <span className="h-5 w-5 rounded-full grid place-items-center text-[9px] font-bold" style={{ background: i === 0 ? "#00DBD5" : "#DFE5ED", color: i === 0 ? "#fff" : "#162744" }}>{i + 1}</span>
                    <span className={i === 0 ? "font-semibold" : "opacity-60"}>{step}</span>
                  </span>
                ))}
              </div>
            </div>
            <button onClick={onClose} className="w-full py-2.5 rounded-xl text-sm font-semibold text-white" style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}>Cerrar</button>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-3">
            {isSuperAdmin && (
              <div>
                <label htmlFor="invite-tenant" className="text-xs font-semibold block mb-1">Empresa u organismo destino *</label>
                <select
                  id="invite-tenant"
                  required
                  value={selectedTenantId}
                  onChange={(e) => { setSelectedTenantId(e.target.value); setSelectedRoleIds([]); }}
                  disabled={tenantsLoading}
                  className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
                >
                  <option value="">{tenantsLoading ? "Cargando…" : "Seleccionar destino…"}</option>
                  {companies.length > 0 && (
                    <optgroup label="Compañías">
                      {companies.map((c) => (
                        <option key={c.id} value={c.id}>{c.name}</option>
                      ))}
                    </optgroup>
                  )}
                  {transitOfficeTenants.length > 0 && (
                    <optgroup label="Organismos de Tránsito">
                      {transitOfficeTenants.map((t) => (
                        <option key={t.id} value={t.id}>{t.legalName} ({t.transitOfficeCode})</option>
                      ))}
                    </optgroup>
                  )}
                </select>
              </div>
            )}
            <div>
              <label htmlFor="invite-name" className="text-xs font-semibold block mb-1">Nombre completo *</label>
              <input
                id="invite-name"
                type="text"
                required
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                placeholder="Juan Pérez"
                className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
              />
            </div>
            <div>
              <label htmlFor="invite-email" className="text-xs font-semibold block mb-1">Correo electrónico *</label>
              <input
                id="invite-email"
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="correo@empresa.com"
                className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
              />
            </div>
            {isSuperAdmin ? (
              <div className="flex items-center gap-2 px-3 py-2.5 rounded-xl border text-xs" style={{ borderColor: "#00DBD5", background: "rgba(0,219,213,0.06)" }}>
                {isSelectedTenantOt
                  ? <Landmark className="h-3.5 w-3.5 shrink-0" style={{ color: "#00DBD5" }} />
                  : <Shield className="h-3.5 w-3.5 shrink-0" style={{ color: "#00DBD5" }} />}
                <span>
                  Se creará como{" "}
                  <strong>{isSelectedTenantOt ? "Administrador OT" : "Administrador de Compañía"}</strong>
                </span>
              </div>
            ) : rolesLoading ? (
              <p className="text-xs opacity-60">Cargando roles…</p>
            ) : roles.length > 0 ? (
              // HU #10510 AC1/AC3: selección MÚLTIPLE de roles (checklist, mismo patrón visual
              // de checkboxes que RbacAdmin.tsx). Al menos un rol es obligatorio — el botón de
              // enviar se deshabilita y se muestra un mensaje de ayuda mientras no haya ninguno.
              <fieldset>
                <legend className="text-xs font-semibold block mb-1">Roles *</legend>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1.5 rounded-xl border px-3 py-2.5">
                  {roles.map((r) => (
                    <label key={r.id} className="flex items-center gap-2 cursor-pointer text-xs">
                      <input
                        type="checkbox"
                        checked={selectedRoleIds.includes(r.id)}
                        onChange={() => toggleRole(r.id)}
                        className="h-3.5 w-3.5 accent-[#557EFF]"
                      />
                      <span>{r.name}</span>
                    </label>
                  ))}
                </div>
                <p className="text-[10px] mt-1" style={{ color: rolesRequiredAndMissing ? "#FF4E00" : undefined, opacity: rolesRequiredAndMissing ? 1 : 0.6 }}>
                  {rolesRequiredAndMissing ? "Selecciona al menos un rol." : "Puedes marcar varios roles."}
                </p>
              </fieldset>
            ) : (
              <p role="alert" className="text-xs py-2 px-3 rounded-xl font-medium" style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}>
                No hay roles configurados para tu empresa. Contacta al Super Admin.
              </p>
            )}
            {error && (
              <p role="alert" className="text-xs py-2 px-3 rounded-xl font-medium" style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}>{error}</p>
            )}
            <div className="rounded-xl p-3 border bg-[rgba(0,219,213,0.06)]">
              <p className="text-[10px] font-semibold uppercase opacity-60 mb-2">Onboarding</p>
              <div className="flex items-center gap-2 text-xs">
                {["Invitación enviada", "Activación", "Primer acceso"].map((step, i) => (
                  <span key={step} className="flex items-center gap-1">
                    <span className="h-5 w-5 rounded-full grid place-items-center text-[9px] font-bold" style={{ background: i === 0 ? "#00DBD5" : "#DFE5ED", color: i === 0 ? "#fff" : "#162744" }}>{i + 1}</span>
                    <span className={i === 0 ? "font-semibold" : "opacity-60"}>{step}</span>
                  </span>
                ))}
              </div>
            </div>
            <button
              type="submit"
              disabled={status === "loading" || (isSuperAdmin && !selectedTenantId) || rolesRequiredAndMissing}
              className="w-full py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-60 transition"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              {status === "loading" ? "Enviando…" : "Enviar Instrucciones"}
            </button>
          </form>
        )}
      </div>
    </div>
  );
}
