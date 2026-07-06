"use client";

import { useCallback, useEffect, useState } from "react";
import { Ban, Clock, Plus, ShieldOff, UserCheck, UserX } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  fetchOtUsers,
  inviteOtUser,
  suspendOtUser,
  unsuspendOtUser,
  type OtUserItem,
} from "@/lib/api/admin-ot-security";
import { ApiError } from "@/lib/api/types";

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
  const [status, setStatus] = useState<UiStatus>("loading");
  const [users, setUsers] = useState<OtUserItem[]>([]);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [suspendTarget, setSuspendTarget] = useState<OtUserItem | null>(null);

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

  async function handleSuspend(userId: string, reason: string, endsAt: string) {
    try {
      await suspendOtUser(userId, { reason, endsAt }, { transitOfficeId });
      show("Usuario suspendido correctamente.", "success");
      setSuspendTarget(null);
      void load();
    } catch {
      show("No se pudo suspender al usuario.", "error");
    }
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
        <button
          type="button"
          onClick={() => setInviteOpen(true)}
          className="flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-semibold text-white transition hover:opacity-90"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <Plus className="h-4 w-4" aria-hidden="true" />
          Invitar usuario
        </button>
      </div>

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
            <Plus className="h-4 w-4" aria-hidden="true" />
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
                    {u.status !== "pending" &&
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
                        <button
                          type="button"
                          title="Suspender usuario"
                          aria-label={`Suspender usuario ${u.fullName}`}
                          onClick={() => setSuspendTarget(u)}
                          className="p-1.5 rounded-lg transition hover:bg-red-50"
                          style={{ color: "#FF4E00" }}
                        >
                          <Ban className="h-4 w-4" />
                        </button>
                      ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>

      {inviteOpen && (
        <OtInviteUserDialog onConfirm={handleInvite} onClose={() => setInviteOpen(false)} />
      )}
      {suspendTarget && (
        <OtSuspendUserDialog
          user={suspendTarget}
          onConfirm={(reason, endsAt) => handleSuspend(suspendTarget.id, reason, endsAt)}
          onClose={() => setSuspendTarget(null)}
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

function OtSuspendUserDialog({
  user,
  onConfirm,
  onClose,
}: {
  user: OtUserItem;
  onConfirm: (reason: string, endsAt: string) => Promise<void>;
  onClose: () => void;
}) {
  const [reason, setReason] = useState("");
  const [endsAt, setEndsAt] = useState("");
  const [busy, setBusy] = useState(false);

  // eslint-disable-next-line react-hooks/purity
  const defaultEndsAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString().slice(0, 16);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    await onConfirm(reason.trim(), new Date(endsAt || defaultEndsAt).toISOString());
    setBusy(false);
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: "rgba(22,39,68,0.45)" }}
      role="dialog"
      aria-modal="true"
      aria-labelledby="ot-suspend-user-title"
    >
      <div className="w-full max-w-md rounded-2xl p-6 shadow-2xl" style={{ background: "#FFFFFF" }}>
        <h2 id="ot-suspend-user-title" className="text-base font-bold mb-1" style={{ color: "#162744" }}>
          Suspender usuario
        </h2>
        <p className="text-xs mb-4" style={{ color: "#557EFF" }}>
          {user.fullName}
        </p>
        <form onSubmit={handleSubmit} className="space-y-4">
          <OtField label="Motivo *" htmlFor="ot-suspend-reason">
            <textarea
              id="ot-suspend-reason"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              required
              rows={3}
              placeholder="Ej. Incumplimiento de políticas"
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-red-400 resize-none"
            />
          </OtField>
          <OtField label="Suspendido hasta *" htmlFor="ot-suspend-ends-at">
            <input
              id="ot-suspend-ends-at"
              type="datetime-local"
              value={endsAt || defaultEndsAt}
              onChange={(e) => setEndsAt(e.target.value)}
              min={new Date().toISOString().slice(0, 16)}
              required
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-red-400"
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
              style={{ background: "#FF4E00" }}
            >
              {busy ? "Aplicando…" : "Suspender"}
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
