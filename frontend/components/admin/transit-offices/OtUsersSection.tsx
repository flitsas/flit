"use client";

import { useCallback, useEffect, useState } from "react";
import { Ban, Clock, History, MailX, Pencil, RotateCcw, ShieldOff, Trash2 } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  fetchOtUsers,
  inviteOtUser,
  suspendOtUser,
  unsuspendOtUser,
  updateOtUser,
  deleteOtUser,
  resendOtInvitation,
  cancelOtInvitation,
  type OtUserItem,
} from "@/lib/api/admin-ot-security";
// HU #10624 — restaurar (POST /api/v1/superadmin/users/{userId}/restore) es un endpoint
// genérico SOLO SuperAdmin, sin scope OT: se reutiliza el mismo cliente que Usuarios.tsx.
import { restoreUser } from "@/lib/api/security";
import { ApiError } from "@/lib/api/types";
import { usePermissions } from "@/hooks/usePermissions";
import { EditUserModal } from "@/components/atom/modules/users/EditUserModal";
import { DeleteUserDialog } from "@/components/atom/modules/users/DeleteUserDialog";
import { RestoreUserDialog } from "@/components/atom/modules/users/RestoreUserDialog";
import { ResendInvitationButton } from "@/components/atom/modules/users/ResendInvitationButton";
import { CancelInvitationDialog } from "@/components/atom/modules/users/CancelInvitationDialog";
import { UsersTable, toUserRow } from "@/components/atom/modules/users/UsersTable";
import { UserAuditHistoryDrawer } from "@/components/atom/modules/users/UserAuditHistoryDrawer";
// HU19 — misma area clickeable minima que la columna de acciones unificada (RowActions).
import { ICON_BUTTON_HIT_AREA, type RowAction } from "@/components/atom/RowActions";
import { assignRole, getRoles, type TenantRole } from "@/lib/api/security";
import { superadminClient } from "@/lib/api/superadmin-client";
import { formatOtDate } from "./ot-utils";
import {
  SuspendOrDeactivateModal,
  type SuspendMode,
} from "@/components/atom/modules/users/SuspendOrDeactivateModal";

export interface OtUsersSectionProps {
  transitOfficeId: string;
}

/**
 * Pestaña "Usuarios" del hub OT. Self-service: listar, invitar eligiendo rol del catálogo
 * TRANSIT_OFFICE, editar (incluido el rol) y suspender/reactivar. Usa la misma UsersTable que
 * el módulo Usuarios y la ficha de compañía, para que las tres pantallas se vean y se
 * comporten igual; el listado de eliminados conserva su propia tabla y UiStateBoundary.
 */
export function OtUsersSection({ transitOfficeId }: OtUsersSectionProps) {
  const { show } = useToast();
  const { isSuperAdmin, userId: currentUserId } = usePermissions();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [users, setUsers] = useState<OtUserItem[]>([]);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [suspendTarget, setSuspendTarget] = useState<{ user: OtUserItem; mode: SuspendMode } | null>(null);
  const [editTarget, setEditTarget] = useState<OtUserItem | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<OtUserItem | null>(null);
  // HU #10623 (AC3/AC4): "Ver eliminados" es exclusivo de SuperAdmin — este hub no tiene tabs
  // (a diferencia de Usuarios.tsx), así que se ofrece como toggle en el header.
  const [showDeleted, setShowDeleted] = useState(false);
  // HU #10624 (AC3) — listado real de eliminados del tenant OT resuelto.
  const [deletedStatus, setDeletedStatus] = useState<UiStatus>("loading");
  const [deletedUsers, setDeletedUsers] = useState<OtUserItem[]>([]);
  const [restoreTarget, setRestoreTarget] = useState<OtUserItem | null>(null);
  // HU #10628 — objetivo del diálogo de confirmación "Cancelar invitación" (filas "Pendiente").
  const [cancelTarget, setCancelTarget] = useState<OtUserItem | null>(null);
  const [auditTarget, setAuditTarget] = useState<OtUserItem | null>(null);
  // Roles asignables al editar: catálogo TRANSIT_OFFICE (SuperAdmin) o los del propio tenant.
  const [editRoles, setEditRoles] = useState<TenantRole[]>([]);
  const [editRolesLoading, setEditRolesLoading] = useState(false);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const result = await fetchOtUsers({ transitOfficeId }, signal);
        if (signal?.aborted) {
          return;
        }
        setUsers(result.data);
        setStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [transitOfficeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  // HU #10624 (AC3) — GET /api/v1/admin/ot/users?onlyDeleted=true, EXCLUSIVO de SuperAdmin.
  const loadDeleted = useCallback(
    async (signal?: AbortSignal) => {
      setDeletedStatus("loading");
      try {
        const result = await fetchOtUsers({ transitOfficeId }, signal, true);
        if (signal?.aborted) {
          return;
        }
        setDeletedUsers(result.data);
        setDeletedStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setDeletedStatus("error");
        }
      }
    },
    [transitOfficeId],
  );

  useEffect(() => {
    if (!showDeleted || !isSuperAdmin) return;
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga al activar el toggle
    void loadDeleted(controller.signal);
    return () => controller.abort();
  }, [showDeleted, isSuperAdmin, loadDeleted]);

  useEffect(() => {
    if (!editTarget || editTarget.status === "pending") {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setEditRoles([]);
      return;
    }
    let cancelled = false;
    setEditRolesLoading(true);
    const load = isSuperAdmin
      ? superadminClient.listRoles("TRANSIT_OFFICE").then((list) =>
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
        )
      : getRoles();

    void load
      .then((list) => {
        if (!cancelled) setEditRoles(list);
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
  }, [editTarget, isSuperAdmin]);

  // Acciones de la fila como datos: UsersTable las renderiza con RowActions, que ya trae el
  // área de clic de 40x40 (HU19).
  function actionsForUser(userId: string): RowAction[] {
    const u = users.find((x) => x.id === userId);
    if (!u) return [];

    const actions: RowAction[] = [];

    if (u.status !== "pending" && isSuperAdmin) {
      actions.push({
        icon: History,
        label: `Ver historial de ${u.fullName}`,
        tone: "primary",
        onClick: () => setAuditTarget(u),
      });
    }

    // AC4 (HU #10622): sin "Editar" para pendientes — no hay cuenta real que editar.
    if (u.status !== "pending") {
      actions.push({
        icon: Pencil,
        label: `Editar usuario ${u.fullName}`,
        tone: "primary",
        onClick: () => setEditTarget(u),
      });
    }

    // Bloquear/desactivar/reactivar es EXCLUSIVO de SuperAdmin.
    if (u.status !== "pending" && isSuperAdmin) {
      if (u.isSuspended) {
        actions.push({
          icon: ShieldOff,
          label: `Reactivar usuario ${u.fullName}`,
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

    // AC2 (HU #10623): eliminar es exclusivo de SuperAdmin y nunca sobre la propia fila.
    if (u.status !== "pending" && isSuperAdmin && u.id !== currentUserId) {
      actions.push({
        icon: Trash2,
        label: `Eliminar usuario ${u.fullName}`,
        tone: "danger",
        onClick: () => setDeleteTarget(u),
      });
    }

    // AC2 (HU #10628): "Cancelar invitación" SOLO en filas pendientes.
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

  async function handleInvite(email: string, fullName: string, roleIds: string[]) {
    try {
      await inviteOtUser(
        { email, fullName: fullName || undefined, roleIds: roleIds.length > 0 ? roleIds : undefined },
        { transitOfficeId },
      );
      show(`Invitación enviada a ${email}.`, "success");
      setInviteOpen(false);
      void load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        show("Ya existe una invitación pendiente para ese correo o ya tiene cuenta.", "error");
      } else {
        show("No se pudo enviar la invitación.", "error");
      }
    }
  }

  // Nota (HU #10620/AC3): a diferencia de `handleInvite`/`handleUnsuspend`, aquí NO se
  // atrapa el error con un toast genérico — se deja propagar para que
  // `SuspendOrDeactivateModal` lo mapee (p. ej. "último administrador activo") y lo
  // muestre inline, igual que en el módulo de Compañía (paridad AC4).
  async function handleSuspend(userId: string, reason: string, endsAt: string | null) {
    await suspendOtUser(userId, { reason, endsAt }, { transitOfficeId });
    show(endsAt === null ? "Usuario desactivado correctamente." : "Usuario suspendido correctamente.", "success");
    setSuspendTarget(null);
    void load();
  }

  async function handleUnsuspend(userId: string) {
    try {
      await unsuspendOtUser(userId, { transitOfficeId });
      show("Usuario reactivado correctamente.", "success");
      void load();
    } catch {
      show("No se pudo reactivar al usuario.", "error");
    }
  }

  // Inyectado a EditUserModal — liga el scope OT (transitOfficeId) a updateOtUser. Los
  // errores 409/404 los mapea el propio modal (no se atrapan aquí para no perder el
  // mensaje específico ni el texto escrito en el formulario, AC2/AC3).
  function handleUpdateUser(
    userId: string,
    payload: { displayName?: string; email?: string; rowVersion: number },
  ) {
    return updateOtUser(userId, payload, { transitOfficeId });
  }

  // HU #10623 — Inyectado a DeleteUserDialog, mismo patrón que handleUpdateUser: liga el
  // scope OT a deleteOtUser. El diálogo mapea los errores 400/409 (AC1 defensa en profundidad).
  function handleDeleteUser(userId: string, rowVersion: number) {
    return deleteOtUser(userId, { rowVersion }, { transitOfficeId });
  }

  // HU #10624 (AC3) — inyectado a RestoreUserDialog; la confirmación vive en el diálogo.
  function handleRestoreUser(userId: string) {
    return restoreUser(userId);
  }

  // HU #10626 — Inyectado a ResendInvitationButton: liga el scope OT a resendOtInvitation. El
  // propio botón mapea 409/429 (AC2) y aplica el cooldown visual (AC1); aquí solo se persiste.
  function handleResendInvitation(invitationId: string) {
    return resendOtInvitation(invitationId, { transitOfficeId });
  }

  // HU #10628 — Inyectado a CancelInvitationDialog: liga el scope OT a cancelOtInvitation. La
  // confirmación (distinta de "Eliminar usuario", AC2) y el mapeo de errores 404/409 (AC3)
  // viven en el propio diálogo; aquí solo se persiste.
  function handleCancelInvitation(invitationId: string) {
    return cancelOtInvitation(invitationId, { transitOfficeId });
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-sm font-bold text-foreground">
            Usuarios
          </h2>
          <p className="text-xs mt-0.5" style={{ color: "#557EFF" }}>
            Invita colaboradores a este organismo de tránsito y gestiona su acceso.
          </p>
        </div>
        <div className="flex items-center gap-2">
          {/* AC4 (HU #10623): "Ver eliminados" es exclusivo de SuperAdmin — ot_admin nunca lo ve. */}
          {isSuperAdmin && (
            <button
              type="button"
              onClick={() => setShowDeleted((v) => !v)}
              aria-pressed={showDeleted}
              className="flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-semibold border transition hover:bg-blue-50"
              style={{ color: "#557EFF" }}
            >
              <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
              {showDeleted ? "Ver usuarios activos" : "Ver eliminados"}
            </button>
          )}
          <button
            type="button"
            onClick={() => setInviteOpen(true)}
            className="flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-semibold text-white transition hover:opacity-90"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            Invitar usuario
          </button>
        </div>
      </div>

      {showDeleted && isSuperAdmin && (
        // HU #10624 (AC3) — GET /api/v1/admin/ot/users?onlyDeleted=true: usuarios eliminados
        // del tenant OT resuelto. Restaurar (1 clic de confirmación en RestoreUserDialog) deshace
        // el soft-delete vía el endpoint genérico restoreUser() (SOLO SuperAdmin).
        <UiStateBoundary
          status={deletedStatus}
          emptyMessage="No hay usuarios eliminados en este organismo de tránsito."
          errorMessage="No se pudo cargar el listado de usuarios eliminados."
          onRetry={() => void loadDeleted()}
        >
          <div className="rounded-xl border overflow-hidden bg-card">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b bg-muted">
                  <th className="px-4 py-3 text-left text-xs font-semibold" style={{ color: "#557EFF" }}>
                    Usuario
                  </th>
                  <th className="px-4 py-3 text-left text-xs font-semibold" style={{ color: "#557EFF" }}>
                    Eliminado el
                  </th>
                  <th className="px-4 py-3 text-right text-xs font-semibold" style={{ color: "#557EFF" }}>
                    Acciones
                  </th>
                </tr>
              </thead>
              <tbody>
                {deletedUsers.map((u) => (
                  <tr
                    key={u.id}
                    className="transition hover:bg-blue-50/40 dark:hover:bg-white/5 border-b"
                  >
                    <td className="px-4 py-3">
                      <div className="font-medium text-sm">{u.fullName}</div>
                      <div className="text-xs" style={{ color: "#557EFF" }}>
                        {u.email}
                      </div>
                    </td>
                    <td className="px-4 py-3 opacity-70">{u.deletedAt ? formatOtDate(u.deletedAt) : "—"}</td>
                    <td className="px-4 py-3 text-right">
                      <button
                        type="button"
                        title="Restaurar usuario"
                        aria-label={`Restaurar usuario ${u.fullName}`}
                        onClick={() => setRestoreTarget(u)}
                        className={`${ICON_BUTTON_HIT_AREA} p-1.5 rounded-lg transition hover:bg-blue-50`}
                        style={{ color: "#557EFF" }}
                      >
                        <RotateCcw className="h-4 w-4" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </UiStateBoundary>
      )}

      {!showDeleted && (
        // Misma tabla, mismos filtros y mismas acciones que el módulo Usuarios y que la ficha
        // de compañía: antes esta sección tenía su propia <table>, sin filtros y con chips de
        // estado distintos, y por eso "no se parecía a las otras".
        <UsersTable
          rows={users.map((u) =>
            toUserRow(u, { profile: "OT", tenantType: "TRANSIT_OFFICE" }),
          )}
          loading={status === "loading"}
          error={status === "error" ? "No se pudo cargar el listado de usuarios." : null}
          onRetry={() => void load()}
          emptyMessage="No hay usuarios en este organismo de tránsito todavía."
          actionsFor={(row) => actionsForUser(row.id)}
          extraActionsFor={(row) =>
            // AC3 (HU #10626): SOLO en filas "Pendiente" — el id de la fila ya es el invitationId.
            row.status === "pending" ? (
              <ResendInvitationButton
                invitationId={row.id}
                fullName={row.fullName}
                resend={handleResendInvitation}
                onResent={(outcome) =>
                  show(
                    outcome.emailSent
                      ? `Invitación reenviada a ${outcome.email}.`
                      : "Invitación reenviada, pero el correo no pudo entregarse.",
                    "success",
                  )
                }
              />
            ) : null
          }
        />
      )}

      {inviteOpen && (
        <OtInviteUserDialog
          onConfirm={handleInvite}
          onClose={() => setInviteOpen(false)}
          isSuperAdmin={isSuperAdmin}
        />
      )}
      {suspendTarget && (
        <SuspendOrDeactivateModal
          user={suspendTarget.user}
          mode={suspendTarget.mode}
          onConfirm={(reason, endsAt) => handleSuspend(suspendTarget.user.id, reason, endsAt)}
          onClose={() => setSuspendTarget(null)}
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
            show("Usuario actualizado correctamente.", "success");
            void load();
          }}
          onUpdate={handleUpdateUser}
          // Toda esta sección vive dentro de un organismo: el perfil no depende de la fila.
          profile="OT"
          roleSection={{
            currentRoleName: editTarget.role,
            currentRoleId: editTarget.roleId,
            roles: editRoles,
            rolesLoading: editRolesLoading,
            onAssignRole: async (roleId) => {
              await assignRole(editTarget.id, roleId);
              show("Rol actualizado correctamente.", "success");
              await load();
            },
          }}
        />
      )}
      {auditTarget && isSuperAdmin && (
        <UserAuditHistoryDrawer
          userId={auditTarget.id}
          userLabel={auditTarget.fullName}
          onClose={() => setAuditTarget(null)}
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
            show("Usuario eliminado correctamente.", "success");
            void load();
          }}
          onDelete={handleDeleteUser}
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
            show("Invitación cancelada correctamente.", "success");
            void load();
          }}
          onStale={() => void load()}
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
            show("Usuario restaurado correctamente.", "success");
            void loadDeleted();
            // Ajuste QA: sin este refresco, el listado activo quedaba con el estado viejo
            // (sin el usuario restaurado) al volver a "Ver usuarios activos".
            void load();
          }}
          onRestore={handleRestoreUser}
        />
      )}
    </div>
  );
}


/**
 * Alta de usuario del organismo. Ya no fuerza `ot_admin` a ciegas: ofrece los roles del
 * catálogo TRANSIT_OFFICE y, si no se marca ninguno, el backend asigna el rol de sistema
 * (comportamiento previo).
 */
function OtInviteUserDialog({
  onConfirm,
  onClose,
  isSuperAdmin,
}: {
  onConfirm: (email: string, fullName: string, roleIds: string[]) => Promise<void>;
  onClose: () => void;
  isSuperAdmin: boolean;
}) {
  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [busy, setBusy] = useState(false);
  const [roles, setRoles] = useState<TenantRole[]>([]);
  const [rolesLoading, setRolesLoading] = useState(true);
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>([]);

  // El SuperAdmin lee el catálogo global; el ot_admin usa /security/roles, que ya resuelve
  // TRANSIT_OFFICE por su propio tenant.
  useEffect(() => {
    let cancelled = false;
    const load = isSuperAdmin
      ? superadminClient.listRoles("TRANSIT_OFFICE").then((list) =>
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
        )
      : getRoles();

    void load
      .then((list) => {
        if (!cancelled) setRoles(list);
      })
      .catch(() => {
        if (!cancelled) setRoles([]);
      })
      .finally(() => {
        if (!cancelled) setRolesLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [isSuperAdmin]);

  function toggleRole(roleId: string) {
    setSelectedRoleIds((prev) =>
      prev.includes(roleId) ? prev.filter((id) => id !== roleId) : [...prev, roleId],
    );
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    await onConfirm(email.trim(), fullName.trim(), selectedRoleIds);
    setBusy(false);
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: "rgba(22,39,68,0.45)" }}
      role="dialog"
      aria-modal="true"
      aria-labelledby="ot-invite-user-title"
    >
      <div className="w-full max-w-md rounded-2xl p-6 shadow-2xl bg-card">
        <h2 id="ot-invite-user-title" className="text-base font-bold mb-1 text-foreground">
          Invitar usuario
        </h2>
        <p className="text-xs mb-4 text-muted-foreground">
          Perfil <strong>Organismo de Tránsito</strong>. Elige los roles con los que entra el
          usuario.
        </p>
        <form onSubmit={handleSubmit} className="space-y-4">
          <OtField label="Nombre completo" htmlFor="ot-invite-name">
            <input
              id="ot-invite-name"
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              required
              placeholder="Ej. Laura García"
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-blue-400"
            />
          </OtField>
          <OtField label="Correo electrónico" htmlFor="ot-invite-email">
            <input
              id="ot-invite-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              placeholder="laura@transito.gov.co"
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-blue-400"
            />
          </OtField>
          {rolesLoading ? (
            <p className="text-xs opacity-60">Cargando roles…</p>
          ) : roles.length > 0 ? (
            <fieldset>
              <legend className="block text-xs font-medium mb-1 text-foreground">Roles</legend>
              <div className="grid grid-cols-1 gap-x-4 gap-y-1.5 rounded-lg border px-3 py-2.5 sm:grid-cols-2">
                {roles.map((r) => (
                  <label key={r.id} className="flex cursor-pointer items-center gap-2 text-xs">
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
              <p className="mt-1 text-[10px] opacity-60">
                Sin selección se asigna el rol de Administrador OT.
              </p>
            </fieldset>
          ) : (
            <p className="text-xs opacity-60">
              No hay roles configurados para organismos de tránsito. Se asignará Administrador OT.
            </p>
          )}
          <div className="flex gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              disabled={busy}
              className="flex-1 py-2 rounded-lg text-sm font-medium border transition hover:bg-gray-50 disabled:opacity-60 text-foreground"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={busy}
              className="flex-1 py-2 rounded-lg text-sm font-medium text-white transition hover:opacity-90 disabled:opacity-60"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              {busy ? "Enviando…" : "Enviar invitación"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function OtField({
  label,
  htmlFor,
  children,
}: {
  label: string;
  htmlFor: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className="block text-xs font-medium mb-1 text-foreground">
        {label}
      </label>
      {children}
    </div>
  );
}
