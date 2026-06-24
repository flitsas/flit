"use client";

import { useCallback, useEffect, useState } from "react";
import { Plus, UserCheck, UserX, Clock, ChevronDown, Ban, ShieldOff } from "lucide-react";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  getUsers,
  getRoles,
  assignRole,
  createInvitation,
  blockUser,
  unblockUser,
  type TenantUser,
  type TenantRole,
} from "@/lib/api/security";
import { ApiError } from "@/lib/api/types";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import type { CompanyListItem } from "@/lib/api/types";
import { superadminClient, type RbacRole } from "@/lib/api/superadmin-client";
import { usePermissions } from "@/hooks/usePermissions";

export default function EmpresaUsuariosPage() {
  return (
    <ToastProvider>
      <UsuariosList />
    </ToastProvider>
  );
}

function UsuariosList() {
  const { show } = useToast();
  const { isSuperAdmin } = usePermissions();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [users, setUsers] = useState<TenantUser[]>([]);
  const [roles, setRoles] = useState<TenantRole[]>([]);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [suspendTarget, setSuspendTarget] = useState<TenantUser | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const [u, r] = await Promise.all([getUsers(), getRoles()]);
      if (signal?.aborted) return;
      setUsers(u);
      setRoles(r);
      setStatus(u.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, []);

  useEffect(() => {
    const ctrl = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(ctrl.signal);
    return () => ctrl.abort();
  }, [load]);

  async function handleAssignRole(userId: string, roleId: string) {
    try {
      await assignRole(userId, roleId);
      show("Rol asignado correctamente.", "success");
      void load();
    } catch {
      show("No se pudo asignar el rol.", "error");
    }
  }

  async function handleBlock(userId: string, reason: string, endsAt: string) {
    try {
      await blockUser(userId, reason, endsAt);
      show("Usuario bloqueado correctamente.", "success");
      setSuspendTarget(null);
      void load();
    } catch {
      show("No se pudo bloquear el usuario.", "error");
    }
  }

  async function handleUnblock(userId: string) {
    try {
      await unblockUser(userId);
      show("Usuario desbloqueado correctamente.", "success");
      void load();
    } catch {
      show("No se pudo desbloquear el usuario.", "error");
    }
  }

  async function handleInvite(email: string, fullName: string, roleId?: string, targetTenantId?: string) {
    try {
      await createInvitation(email, fullName, roleId, targetTenantId);
      show(`Invitación enviada a ${email}.`, "success");
      setInviteOpen(false);
      void load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        show("Ya existe una invitación pendiente para ese correo.", "error");
      } else {
        show("No se pudo enviar la invitación.", "error");
      }
    }
  }

  return (
    <div className="max-w-5xl mx-auto space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold" style={{ color: "#162744" }}>Usuarios</h1>
          <p className="text-xs mt-0.5" style={{ color: "#557EFF" }}>
            Gestiona los usuarios y sus roles en tu empresa
          </p>
        </div>
        <button
          onClick={() => setInviteOpen(true)}
          className="flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm font-medium text-white transition hover:opacity-90"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <Plus className="h-4 w-4" />
          Invitar usuario
        </button>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="No hay usuarios en tu empresa todavía."
        emptyCta={
          <button
            onClick={() => setInviteOpen(true)}
            className="mt-3 flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm font-medium text-white"
            style={{ background: "#557EFF" }}
          >
            <Plus className="h-4 w-4" />
            Invitar usuario
          </button>
        }
        onRetry={() => void load()}
      >
        <div
          className="rounded-xl border overflow-hidden"
          style={{ borderColor: "#DFE5ED", background: "#FFFFFF" }}
        >
          <table className="w-full text-sm">
            <thead>
              <tr style={{ borderBottom: "1px solid #DFE5ED", background: "#F7F9FC" }}>
                <th className="px-4 py-3 text-left text-xs font-semibold" style={{ color: "#557EFF" }}>Usuario</th>
                <th className="px-4 py-3 text-left text-xs font-semibold" style={{ color: "#557EFF" }}>Estado</th>
                <th className="px-4 py-3 text-left text-xs font-semibold" style={{ color: "#557EFF" }}>Rol</th>
                <th className="px-4 py-3 text-right text-xs font-semibold" style={{ color: "#557EFF" }}>Acciones</th>
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
                    <div className="text-xs" style={{ color: "#557EFF" }}>{u.email}</div>
                  </td>
                  <td className="px-4 py-3">
                    <StatusBadge status={u.status} />
                  </td>
                  <td className="px-4 py-3">
                    {u.status !== "pending" ? (
                      <RoleSelector
                        currentRoleId={u.roleId}
                        currentRoleName={u.role}
                        roles={roles}
                        onChange={(rid) => handleAssignRole(u.id, rid)}
                      />
                    ) : (
                      <span className="text-xs" style={{ color: "#9CA3AF" }}>—</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-right">
                    {u.status !== "pending" && (
                      u.isSuspended ? (
                        <button
                          title="Desbloquear usuario"
                          onClick={() => handleUnblock(u.id)}
                          className="p-1.5 rounded-lg transition hover:bg-green-50"
                          style={{ color: "#00DBD5" }}
                        >
                          <ShieldOff className="h-4 w-4" />
                        </button>
                      ) : (
                        <button
                          title="Bloquear usuario"
                          onClick={() => setSuspendTarget(u)}
                          className="p-1.5 rounded-lg transition hover:bg-red-50"
                          style={{ color: "#FF4E00" }}
                        >
                          <Ban className="h-4 w-4" />
                        </button>
                      )
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>

      {inviteOpen && (
        <InviteDialog
          roles={roles}
          isSuperAdmin={isSuperAdmin}
          onConfirm={handleInvite}
          onClose={() => setInviteOpen(false)}
        />
      )}
      {suspendTarget && (
        <SuspendDialog
          user={suspendTarget}
          onConfirm={(reason, endsAt) => handleBlock(suspendTarget.id, reason, endsAt)}
          onClose={() => setSuspendTarget(null)}
        />
      )}
    </div>
  );
}

function StatusBadge({ status }: { status: TenantUser["status"] }) {
  if (status === "active") {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium" style={{ background: "rgba(0,219,213,0.12)", color: "#00948F" }}>
        <UserCheck className="h-3 w-3" />
        Activo
      </span>
    );
  }
  if (status === "pending") {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium" style={{ background: "rgba(85,126,255,0.12)", color: "#557EFF" }}>
        <Clock className="h-3 w-3" />
        Pendiente
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium" style={{ background: "rgba(255,78,0,0.10)", color: "#FF4E00" }}>
      <UserX className="h-3 w-3" />
      Inactivo
    </span>
  );
}

function RoleSelector({
  currentRoleId,
  currentRoleName,
  roles,
  onChange,
}: {
  currentRoleId: string | null;
  currentRoleName: string | null;
  roles: TenantRole[];
  onChange: (roleId: string) => void;
}) {
  const [open, setOpen] = useState(false);

  return (
    <div className="relative inline-block">
      <button
        onClick={() => setOpen((v) => !v)}
        className="flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-medium border transition hover:border-blue-400"
        style={{ borderColor: "#DFE5ED", color: "#162744" }}
      >
        {currentRoleName ?? "Sin rol"}
        <ChevronDown className="h-3 w-3 opacity-60" />
      </button>
      {open && (
        <div
          className="absolute left-0 top-full mt-1 z-20 rounded-xl py-1 w-48 shadow-lg"
          style={{ background: "#FFFFFF", border: "1px solid #DFE5ED" }}
        >
          {roles.map((r) => (
            <button
              key={r.id}
              onClick={() => {
                onChange(r.id);
                setOpen(false);
              }}
              className="w-full text-left px-3 py-2 text-xs hover:bg-blue-50 transition"
              style={{ color: r.id === currentRoleId ? "#557EFF" : "#162744", fontWeight: r.id === currentRoleId ? 600 : 400 }}
            >
              {r.name}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function InviteDialog({
  roles,
  isSuperAdmin,
  onConfirm,
  onClose,
}: {
  roles: TenantRole[];
  isSuperAdmin: boolean;
  onConfirm: (email: string, fullName: string, roleId?: string, targetTenantId?: string) => void;
  onClose: () => void;
}) {
  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [roleId, setRoleId] = useState("");
  const [selectedTenantId, setSelectedTenantId] = useState("");
  const [companies, setCompanies] = useState<{ id: string; name: string }[]>([]);
  const [tenantRoles, setTenantRoles] = useState<{ id: string; name: string }[]>([]);
  const [tenantsLoading, setTenantsLoading] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!isSuperAdmin) return;
    setTenantsLoading(true);
    fetchCompaniesIndex({ pageSize: 200 })
      .then((r) => setCompanies(r.data.map((c: CompanyListItem) => ({ id: c.id, name: c.razonSocial }))))
      .catch(() => { /* silencioso */ })
      .finally(() => setTenantsLoading(false));
  }, [isSuperAdmin]);

  useEffect(() => {
    if (!selectedTenantId) { setTenantRoles([]); return; }
    superadminClient.listRoles(selectedTenantId)
      .then((r) => setTenantRoles((r as RbacRole[]).map((role) => ({ id: role.id, name: role.name }))))
      .catch(() => setTenantRoles([]));
  }, [selectedTenantId]);

  const rolesForDropdown = isSuperAdmin ? tenantRoles : roles;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    await onConfirm(
      email,
      fullName,
      roleId || undefined,
      isSuperAdmin ? selectedTenantId || undefined : undefined,
    );
    setBusy(false);
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" style={{ background: "rgba(22,39,68,0.45)" }}>
      <div className="w-full max-w-md rounded-2xl p-6 shadow-2xl" style={{ background: "#FFFFFF" }}>
        <h2 className="text-base font-bold mb-4" style={{ color: "#162744" }}>Invitar usuario</h2>
        <form onSubmit={handleSubmit} className="space-y-4">
          {isSuperAdmin && (
            <Field label="Empresa destino *">
              <select
                value={selectedTenantId}
                onChange={(e) => { setSelectedTenantId(e.target.value); setRoleId(""); }}
                required
                disabled={tenantsLoading}
                className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-blue-400"
                style={{ borderColor: "#DFE5ED" }}
              >
                <option value="">{tenantsLoading ? "Cargando empresas…" : "Seleccionar empresa…"}</option>
                {companies.map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>
            </Field>
          )}
          <Field label="Nombre completo">
            <input
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              required
              placeholder="Ej. Laura García"
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-blue-400"
              style={{ borderColor: "#DFE5ED" }}
            />
          </Field>
          <Field label="Correo electrónico">
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              placeholder="laura@empresa.com"
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-blue-400"
              style={{ borderColor: "#DFE5ED" }}
            />
          </Field>
          <Field label="Rol (opcional)">
            <select
              value={roleId}
              onChange={(e) => setRoleId(e.target.value)}
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-blue-400"
              style={{ borderColor: "#DFE5ED" }}
            >
              <option value="">Sin rol asignado</option>
              {rolesForDropdown.map((r) => (
                <option key={r.id} value={r.id}>{r.name}</option>
              ))}
            </select>
          </Field>
          <div className="flex gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="flex-1 py-2 rounded-lg text-sm font-medium border transition hover:bg-gray-50"
              style={{ borderColor: "#DFE5ED", color: "#162744" }}
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

function SuspendDialog({
  user,
  onConfirm,
  onClose,
}: {
  user: TenantUser;
  onConfirm: (reason: string, endsAt: string) => Promise<void>;
  onClose: () => void;
}) {
  const [reason, setReason] = useState("");
  const [endsAt, setEndsAt] = useState("");
  const [busy, setBusy] = useState(false);

  const defaultEndsAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000)
    .toISOString()
    .slice(0, 16);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    await onConfirm(reason.trim(), new Date(endsAt || defaultEndsAt).toISOString());
    setBusy(false);
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" style={{ background: "rgba(22,39,68,0.45)" }}>
      <div className="w-full max-w-md rounded-2xl p-6 shadow-2xl" style={{ background: "#FFFFFF" }}>
        <h2 className="text-base font-bold mb-1" style={{ color: "#162744" }}>Bloquear usuario</h2>
        <p className="text-xs mb-4" style={{ color: "#557EFF" }}>{user.fullName}</p>
        <form onSubmit={handleSubmit} className="space-y-4">
          <Field label="Motivo *">
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              required
              rows={3}
              placeholder="Ej. Incumplimiento de políticas"
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-red-400 resize-none"
              style={{ borderColor: "#DFE5ED" }}
            />
          </Field>
          <Field label="Bloqueado hasta *">
            <input
              type="datetime-local"
              value={endsAt || defaultEndsAt}
              onChange={(e) => setEndsAt(e.target.value)}
              min={new Date().toISOString().slice(0, 16)}
              required
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-red-400"
              style={{ borderColor: "#DFE5ED" }}
            />
          </Field>
          <div className="flex gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="flex-1 py-2 rounded-lg text-sm font-medium border transition hover:bg-gray-50"
              style={{ borderColor: "#DFE5ED", color: "#162744" }}
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={busy}
              className="flex-1 py-2 rounded-lg text-sm font-medium text-white transition hover:opacity-90 disabled:opacity-60"
              style={{ background: "#FF4E00" }}
            >
              {busy ? "Aplicando…" : "Bloquear"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="block text-xs font-medium mb-1" style={{ color: "#162744" }}>{label}</label>
      {children}
    </div>
  );
}
