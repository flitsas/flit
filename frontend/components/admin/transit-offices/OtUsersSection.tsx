"use client";

import { useCallback, useEffect, useState } from "react";
import { Ban, Clock, MailX, Pencil, RotateCcw, ShieldOff, Trash2, UserCheck, UserX } from "lucide-react";
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
import { formatOtDate } from "./ot-utils";
import {
  SuspendOrDeactivateModal,
  type SuspendMode,
} from "@/components/atom/modules/users/SuspendOrDeactivateModal";

export interface OtUsersSectionProps {
  transitOfficeId: string;
}

/**
 * Pestaña "Usuarios" del hub OT (refactor adminOT). Self-service: listar, invitar
 * (solo email + nombre — un tenant OT tiene un único rol posible, ot_admin, sin
 * selector de rol) y suspender/reactivar. 4 estados de UI vía UiStateBoundary,
 * mismo patrón que app/empresa/roles/page.tsx.
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

  async function handleInvite(email: string, fullName: string) {
    try {
      await inviteOtUser({ email, fullName: fullName || undefined }, { transitOfficeId });
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
          <h2 className="text-sm font-bold" style={{ color: "#162744" }}>
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
          <div className="rounded-xl border overflow-hidden" style={{ background: "#FFFFFF" }}>
            <table className="w-full text-sm">
              <thead>
                <tr style={{ borderBottom: "1px solid #DFE5ED", background: "#F7F9FC" }}>
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
                    className="transition hover:bg-blue-50/40"
                    style={{ borderBottom: "1px solid #EEF5FF" }}
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
                        className="p-1.5 rounded-lg transition hover:bg-blue-50"
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
      <UiStateBoundary
        status={status}
        emptyMessage="No hay usuarios en este organismo de tránsito todavía."
        emptyCta={
          <button
            type="button"
            onClick={() => setInviteOpen(true)}
            className="mt-3 flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-semibold text-white"
            style={{ background: "#557EFF" }}
          >
            Invitar usuario
          </button>
        }
        errorMessage="No se pudo cargar el listado de usuarios."
        onRetry={() => void load()}
      >
        <div
          className="rounded-xl border overflow-hidden"
          style={{ background: "#FFFFFF" }}
        >
          <table className="w-full text-sm">
            <thead>
              <tr style={{ borderBottom: "1px solid #DFE5ED", background: "#F7F9FC" }}>
                <th className="px-4 py-3 text-left text-xs font-semibold" style={{ color: "#557EFF" }}>
                  Usuario
                </th>
                <th className="px-4 py-3 text-left text-xs font-semibold" style={{ color: "#557EFF" }}>
                  Estado
                </th>
                <th className="px-4 py-3 text-right text-xs font-semibold" style={{ color: "#557EFF" }}>
                  Acciones
                </th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => (
                <tr
                  key={u.id}
                  className="transition hover:bg-blue-50/40"
                  style={{ borderBottom: "1px solid #EEF5FF" }}
                >
                  <td className="px-4 py-3">
                    <div className="font-medium text-sm">{u.fullName}</div>
                    <div className="text-xs" style={{ color: "#557EFF" }}>
                      {u.email}
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <OtUserStatusBadge status={u.status} isSuspended={u.isSuspended} />
                  </td>
                  <td className="px-4 py-3 text-right">
                    <div className="flex items-center justify-end gap-1">
                      {/* AC4 (HU #10622): sin botón "Editar" para usuarios pendientes — todavía
                          no hay una cuenta real que editar. */}
                      {u.status !== "pending" && (
                        <button
                          type="button"
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
                          El ot_admin no puede suspender ni reactivar usuarios de su OT. */}
                      {u.status !== "pending" && isSuperAdmin &&
                        (u.isSuspended ? (
                          <button
                            type="button"
                            title="Reactivar usuario"
                            aria-label={`Reactivar usuario ${u.fullName}`}
                            onClick={() => void handleUnsuspend(u.id)}
                            className="p-1.5 rounded-lg transition hover:bg-green-50"
                            style={{ color: "#00DBD5" }}
                          >
                            <ShieldOff className="h-4 w-4" />
                          </button>
                        ) : (
                          <div className="flex items-center justify-end gap-1">
                            <button
                              type="button"
                              title="Suspender temporalmente"
                              aria-label={`Suspender temporalmente a ${u.fullName}`}
                              onClick={() => setSuspendTarget({ user: u, mode: "temporary" })}
                              className="p-1.5 rounded-lg transition hover:bg-orange-50"
                              style={{ color: "#FF4E00" }}
                            >
                              <Clock className="h-4 w-4" />
                            </button>
                            <button
                              type="button"
                              title="Desactivar indefinidamente"
                              aria-label={`Desactivar indefinidamente a ${u.fullName}`}
                              onClick={() => setSuspendTarget({ user: u, mode: "indefinite" })}
                              className="p-1.5 rounded-lg transition hover:bg-red-50"
                              style={{ color: "#557EFF" }}
                            >
                              <Ban className="h-4 w-4" />
                            </button>
                          </div>
                        ))}
                      {/* Eliminar es EXCLUSIVO de SuperAdmin (el ot_admin no puede).
                          AC2 (HU #10623): sin botón "Eliminar" sobre la propia fila — nunca
                          puede auto-eliminarse. */}
                      {u.status !== "pending" && isSuperAdmin && u.id !== currentUserId && (
                        <button
                          type="button"
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
                          invitationId. */}
                      {u.status === "pending" && (
                        <ResendInvitationButton
                          invitationId={u.id}
                          fullName={u.fullName}
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
                      )}
                      {/* AC2 (HU #10628): "Cancelar invitación" SOLO en filas "Pendiente" —
                          mutuamente excluyente con "Eliminar usuario" (arriba, solo status !== "pending"). */}
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
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>
      )}

      {inviteOpen && (
        <OtInviteUserDialog onConfirm={handleInvite} onClose={() => setInviteOpen(false)} />
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

function OtUserStatusBadge({
  status,
  isSuspended,
}: {
  status: OtUserItem["status"];
  isSuspended: boolean;
}) {
  if (status === "pending") {
    return (
      <span
        className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium"
        style={{ background: "rgba(85,126,255,0.12)", color: "#557EFF" }}
      >
        <Clock className="h-3 w-3" aria-hidden="true" />
        Pendiente
      </span>
    );
  }
  if (isSuspended || status === "inactive") {
    return (
      <span
        className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium"
        style={{ background: "rgba(255,78,0,0.10)", color: "#FF4E00" }}
      >
        <UserX className="h-3 w-3" aria-hidden="true" />
        Suspendido
      </span>
    );
  }
  return (
    <span
      className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium"
      style={{ background: "rgba(0,219,213,0.12)", color: "#00948F" }}
    >
      <UserCheck className="h-3 w-3" aria-hidden="true" />
      Activo
    </span>
  );
}

function OtInviteUserDialog({
  onConfirm,
  onClose,
}: {
  onConfirm: (email: string, fullName: string) => Promise<void>;
  onClose: () => void;
}) {
  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [busy, setBusy] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    await onConfirm(email.trim(), fullName.trim());
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
      <div className="w-full max-w-md rounded-2xl p-6 shadow-2xl" style={{ background: "#FFFFFF" }}>
        <h2 id="ot-invite-user-title" className="text-base font-bold mb-1" style={{ color: "#162744" }}>
          Invitar usuario
        </h2>
        <p className="text-xs mb-4" style={{ color: "#9CA3AF" }}>
          El usuario invitado tendrá el mismo acceso operativo que un Admin OT.
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
          <div className="flex gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              disabled={busy}
              className="flex-1 py-2 rounded-lg text-sm font-medium border transition hover:bg-gray-50 disabled:opacity-60"
              style={{ color: "#162744" }}
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
      <label htmlFor={htmlFor} className="block text-xs font-medium mb-1" style={{ color: "#162744" }}>
        {label}
      </label>
      {children}
    </div>
  );
}
