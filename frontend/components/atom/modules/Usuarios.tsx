"use client";

import { useState, useEffect } from "react";
import { Plus, Search, X, Building2, Users, Shield, Ban, ShieldOff } from "lucide-react";
import { createInvitation, getUsers, getRoles, assignRole, blockUser, unblockUser, TenantUser, TenantRole } from "@/lib/api/security";
import { ApiError } from "@/lib/api/types";
import { ModuleTitle } from "./ModuleTitle";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import type { CompanyListItem } from "@/lib/api/types";
import { superadminClient, type RbacRole } from "@/lib/api/superadmin-client";
import { usePermissions } from "@/hooks/usePermissions";

const COMPANIES = [
  { name: "FLIT SAS", nit: "900.123.456-7", estado: "Activa", plan: "Enterprise", users: 250 },
  { name: "Movilidad Antioquia", nit: "890.456.789-1", estado: "Activa", plan: "Profesional", users: 84 },
  { name: "Transito Sabaneta", nit: "890.111.222-3", estado: "Suspendida", plan: "Básico", users: 12 },
  { name: "Operador Valle", nit: "890.333.444-5", estado: "En prueba", plan: "Trial", users: 6 },
];

const TABS = [
  { id: "usuarios", label: "Usuarios", icon: Users },
  { id: "roles", label: "Roles y permisos", icon: Shield },
  { id: "companias", label: "Compañías", icon: Building2 },
] as const;

type TabId = (typeof TABS)[number]["id"];

const BADGE: Record<string, string> = {
  Activa: "#00DBD5",
  Suspendida: "#FF4E00",
  "En prueba": "#F9AC00",
};

const STATUS_BADGE: Record<TenantUser["status"], { color: string; label: string }> = {
  active: { color: "#00DBD5", label: "Activo" },
  inactive: { color: "#FF4E00", label: "Inactivo" },
  pending: { color: "#F9AC00", label: "Pendiente" },
};

export function Usuarios() {
  const { isSuperAdmin } = usePermissions();
  const [open, setOpen] = useState(false);
  const [tab, setTab] = useState<TabId>("usuarios");
  const [users, setUsers] = useState<TenantUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [roles, setRoles] = useState<TenantRole[]>([]);
  const [rolesLoading, setRolesLoading] = useState(true);
  const [suspendTarget, setSuspendTarget] = useState<TenantUser | null>(null);

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
    // eslint-disable-next-line react-hooks/set-state-in-effect
    loadRoles();
  }, []);

  function handleInviteSuccess() {
    loadUsers();
  }

  async function handleSuspend(userId: string, reason: string, endsAt: string) {
    await blockUser(userId, reason, endsAt);
    loadUsers();
  }

  async function handleUnsuspend(userId: string) {
    await unblockUser(userId);
    loadUsers();
  }

  return (
    <div className="h-full w-full px-6 pt-5 pb-24 flex flex-col gap-4 overflow-hidden">
      <ModuleTitle
        title="Administración de usuarios y permisos"
        subtitle="Gestiona el acceso de tu equipo a la plataforma."
        right={
          tab === "usuarios" ? (
            <button onClick={() => setOpen(true)} className="flex items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white" style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}>
              <Plus className="h-4 w-4" /> Invitar usuario
            </button>
          ) : tab === "companias" ? (
            <button className="flex items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white" style={{ background: "#557EFF" }}>
              <Plus className="h-4 w-4" /> Nueva compañía
            </button>
          ) : null
        }
      />

      <div className="flex items-center gap-1 border-b border-[#DFE5ED] dark:border-white/10 shrink-0">
        {TABS.map((t) => {
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
      </div>

      {tab === "usuarios" && (
        <>
          <div className="flex items-center gap-2 p-2.5 rounded-xl border bg-white dark:bg-[#0B0F14] max-w-md shrink-0" style={{ borderColor: "#DFE5ED" }}>
            <Search className="h-4 w-4 opacity-60" />
            <input placeholder="Buscar por nombre o correo..." className="flex-1 bg-transparent outline-none text-xs" />
          </div>

          <div className="flex-1 min-h-0 flex flex-col">
            <div className="grid grid-cols-12 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl shrink-0" style={{ background: "#DFE5ED", color: "#162744" }}>
              <div className="col-span-4">Usuario</div>
              <div className="col-span-2">Rol</div>
              <div className="col-span-2">Estado</div>
              <div className="col-span-3">Fecha</div>
              <div className="col-span-1" />
            </div>

            <div className="flex-1 overflow-y-auto space-y-2 pt-2">
              {loading && (
                <div className="py-12 text-center text-sm opacity-60">Cargando usuarios…</div>
              )}
              {!loading && error && (
                <div role="alert" className="py-12 text-center text-sm" style={{ color: "#FF4E00" }}>{error}</div>
              )}
              {!loading && !error && users.length === 0 && (
                <div className="py-12 text-center text-sm opacity-60">No hay usuarios en este tenant. Invita al primero.</div>
              )}
              {!loading && !error && users.map((u) => {
                const badge = STATUS_BADGE[u.status];
                return (
                  <div key={u.id} className="grid grid-cols-12 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs" style={{ borderColor: "#DFE5ED" }}>
                    <div className="col-span-4">
                      <p className="font-semibold">{u.fullName}</p>
                      <p className="text-[10px] opacity-60">{u.email}</p>
                    </div>
                    <div className="col-span-2">
                      {u.status !== "pending" ? (
                        <RoleDropdown
                          userId={u.id}
                          currentRoleName={u.role}
                          roles={roles}
                          rolesLoading={rolesLoading}
                          onAssigned={loadUsers}
                        />
                      ) : (
                        <span className="opacity-60">—</span>
                      )}
                    </div>
                    <div className="col-span-2">
                      <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold text-white" style={{ background: badge.color }}>
                        {badge.label}
                      </span>
                    </div>
                    <div className="col-span-3 opacity-70">{u.createdAt ?? "—"}</div>
                    <div className="col-span-1 flex justify-end">
                      {u.status !== "pending" && (
                        u.isSuspended ? (
                          <button
                            title="Desbloquear usuario"
                            onClick={() => handleUnsuspend(u.id)}
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

      {tab === "roles" && (
        <div className="flex-1 min-h-0 flex flex-col gap-3">
          {rolesLoading ? (
            <div className="py-12 text-center text-sm opacity-60">Cargando roles…</div>
          ) : roles.length === 0 ? (
            <div className="py-12 text-center text-sm opacity-60">
              No hay roles configurados para este tenant. Contacta al Super Admin.
            </div>
          ) : (
            <div className="flex-1 min-h-0 flex flex-col">
              <div className="grid grid-cols-12 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl shrink-0" style={{ background: "#DFE5ED", color: "#162744" }}>
                <div className="col-span-2">Código</div>
                <div className="col-span-4">Nombre</div>
                <div className="col-span-4">Descripción</div>
                <div className="col-span-2 text-center">Permisos</div>
              </div>
              <div className="flex-1 overflow-y-auto space-y-2 pt-2">
                {roles.map((r) => (
                  <div key={r.id} className="grid grid-cols-12 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs" style={{ borderColor: "#DFE5ED" }}>
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

      {tab === "companias" && (
        <div className="flex-1 min-h-0 flex flex-col gap-3">
          <div className="grid grid-cols-4 gap-3 shrink-0">
            {[
              ["Compañías activas", 18, "#00DBD5"],
              ["En periodo de prueba", 4, "#F9AC00"],
              ["Suspendidas", 2, "#FF4E00"],
              ["Usuarios totales", 352, "#557EFF"],
            ].map(([l, v, c]) => (
              <div key={l as string} className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
                <p className="text-[11px] opacity-70 font-medium">{l as string}</p>
                <p className="text-3xl font-bold mt-1" style={{ color: c as string }}>{v as number}</p>
              </div>
            ))}
          </div>
          <div className="flex items-center gap-2 p-2.5 rounded-xl border bg-white dark:bg-[#0B0F14] max-w-md shrink-0" style={{ borderColor: "#DFE5ED" }}>
            <Search className="h-4 w-4 opacity-60" />
            <input placeholder="Buscar por nombre o NIT..." className="flex-1 bg-transparent outline-none text-xs" />
          </div>
          <div className="flex-1 min-h-0 flex flex-col">
            <div className="grid grid-cols-12 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl" style={{ background: "#DFE5ED", color: "#162744" }}>
              <div className="col-span-3">Compañía</div>
              <div className="col-span-2">NIT</div>
              <div className="col-span-2">Estado</div>
              <div className="col-span-2">Plan</div>
              <div className="col-span-1">Usuarios</div>
              <div className="col-span-2 text-right">Acciones</div>
            </div>
            <div className="flex-1 overflow-y-auto space-y-2 pt-2">
              {COMPANIES.map((c) => (
                <div key={c.nit} className="grid grid-cols-12 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs" style={{ borderColor: "#DFE5ED" }}>
                  <div className="col-span-3 font-semibold">{c.name}</div>
                  <div className="col-span-2 font-mono opacity-80">{c.nit}</div>
                  <div className="col-span-2">
                    <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold text-white" style={{ background: BADGE[c.estado] }}>{c.estado}</span>
                  </div>
                  <div className="col-span-2 opacity-80">{c.plan}</div>
                  <div className="col-span-1 font-bold" style={{ color: "#557EFF" }}>{c.users}</div>
                  <div className="col-span-2 flex justify-end gap-2">
                    <button className="px-2 py-1 rounded-lg text-[10px] font-semibold border" style={{ borderColor: "#557EFF", color: "#557EFF" }}>Editar</button>
                    <button className="px-2 py-1 rounded-lg text-[10px] font-semibold text-white" style={{ background: "#557EFF" }}>Configurar</button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {open && <InviteModal onClose={() => setOpen(false)} onSuccess={handleInviteSuccess} roles={roles} isSuperAdmin={isSuperAdmin} />}
      {suspendTarget && (
        <SuspendModal
          user={suspendTarget}
          onClose={() => setSuspendTarget(null)}
          onConfirm={async (reason, endsAt) => {
            await handleSuspend(suspendTarget.id, reason, endsAt);
            setSuspendTarget(null);
          }}
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
        style={{ borderColor: "#DFE5ED", minWidth: 100 }}
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

function SuspendModal({
  user,
  onClose,
  onConfirm,
}: {
  user: TenantUser;
  onClose: () => void;
  onConfirm: (reason: string, endsAt: string) => Promise<void>;
}) {
  const [reason, setReason] = useState("");
  const [endsAt, setEndsAt] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // eslint-disable-next-line react-hooks/purity
  const defaultEndsAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000)
    .toISOString()
    .slice(0, 16);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!reason.trim()) return;
    setBusy(true);
    setError(null);
    try {
      await onConfirm(reason.trim(), new Date(endsAt || defaultEndsAt).toISOString());
    } catch {
      setError("No se pudo aplicar la suspensión. Inténtalo de nuevo.");
      setBusy(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-black/40 backdrop-blur-sm px-4">
      <div className="bg-white dark:bg-[#0B0F14] rounded-2xl p-6 w-full max-w-md border" style={{ borderColor: "#DFE5ED" }}>
        <div className="flex items-start justify-between mb-4">
          <div>
            <h3 className="text-lg font-bold">Bloquear usuario</h3>
            <p className="text-xs opacity-70 mt-0.5">
              <strong>{user.fullName}</strong> no podrá iniciar sesión durante el periodo indicado.
            </p>
          </div>
          <button onClick={onClose} aria-label="Cerrar"><X className="h-5 w-5" /></button>
        </div>
        <form onSubmit={handleSubmit} className="space-y-3">
          <div>
            <label className="text-xs font-semibold block mb-1">Motivo de suspensión *</label>
            <textarea
              required
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Ej. Incumplimiento de políticas de uso"
              rows={3}
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#FF4E00] resize-none"
              style={{ borderColor: "#DFE5ED" }}
            />
          </div>
          <div>
            <label className="text-xs font-semibold block mb-1">Bloqueado hasta *</label>
            <input
              type="datetime-local"
              required
              value={endsAt || defaultEndsAt}
              onChange={(e) => setEndsAt(e.target.value)}
              min={new Date().toISOString().slice(0, 16)}
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#FF4E00]"
              style={{ borderColor: "#DFE5ED" }}
            />
          </div>
          {error && (
            <p role="alert" className="text-xs py-2 px-3 rounded-xl font-medium" style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}>{error}</p>
          )}
          <div className="flex gap-2 pt-1">
            <button
              type="button"
              onClick={onClose}
              className="flex-1 py-2.5 rounded-xl text-sm font-semibold border transition"
              style={{ borderColor: "#DFE5ED" }}
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={busy}
              className="flex-1 py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-60 transition"
              style={{ background: "#FF4E00" }}
            >
              {busy ? "Aplicando…" : "Bloquear usuario"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function InviteModal({
  onClose, onSuccess, roles, isSuperAdmin,
}: {
  onClose: () => void;
  onSuccess: () => void;
  roles: TenantRole[];
  isSuperAdmin: boolean;
}) {
  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [selectedRoleId, setSelectedRoleId] = useState("");
  const [selectedTenantId, setSelectedTenantId] = useState("");
  const [companies, setCompanies] = useState<{ id: string; name: string }[]>([]);
  const [tenantRoles, setTenantRoles] = useState<{ id: string; name: string }[]>([]);
  const [tenantsLoading, setTenantsLoading] = useState(false);
  const [status, setStatus] = useState<"idle" | "loading" | "done" | "done_no_email">("idle");
  const [error, setError] = useState<string | null>(null);
  const [invitedEmail, setInvitedEmail] = useState("");

  useEffect(() => {
    if (!isSuperAdmin) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTenantsLoading(true);
    fetchCompaniesIndex({ pageSize: 200 })
      .then((r) => setCompanies(r.data.map((c: CompanyListItem) => ({ id: c.id, name: c.razonSocial }))))
      .catch(() => { /* silencioso */ })
      .finally(() => setTenantsLoading(false));
  }, [isSuperAdmin]);

  useEffect(() => {
    if (!selectedTenantId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setTenantRoles([]);
      return;
    }
    superadminClient.listRoles(selectedTenantId)
      .then((r) => setTenantRoles((r as RbacRole[]).map((role) => ({ id: role.id, name: role.name }))))
      .catch(() => setTenantRoles([]));
  }, [selectedTenantId]);

  const isDone = status === "done" || status === "done_no_email";

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setStatus("loading");
    try {
      const result = await createInvitation(
        email.trim(),
        fullName.trim(),
        selectedRoleId || undefined,
        isSuperAdmin ? selectedTenantId || undefined : undefined,
      );
      setInvitedEmail(result.email);
      setStatus(result.emailSent ? "done" : "done_no_email");
      onSuccess();
    } catch (err) {
      const s = (err as ApiError).status;
      setError(
        s === 409
          ? "Ya existe una invitación pendiente para este correo."
          : s === 404
            ? "El rol especificado no existe en el tenant."
            : "No se pudo enviar la invitación. Inténtalo de nuevo."
      );
      setStatus("idle");
    }
  }

  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-black/40 backdrop-blur-sm px-4">
      <div className="bg-white dark:bg-[#0B0F14] rounded-2xl p-6 w-full max-w-md border" style={{ borderColor: "#DFE5ED" }}>
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
            <div className="rounded-xl p-3 border bg-[rgba(0,219,213,0.06)]" style={{ borderColor: "#DFE5ED" }}>
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
                <label htmlFor="invite-tenant" className="text-xs font-semibold block mb-1">Empresa destino *</label>
                <select
                  id="invite-tenant"
                  required
                  value={selectedTenantId}
                  onChange={(e) => { setSelectedTenantId(e.target.value); setSelectedRoleId(""); }}
                  disabled={tenantsLoading}
                  className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
                  style={{ borderColor: "#DFE5ED" }}
                >
                  <option value="">{tenantsLoading ? "Cargando empresas…" : "Seleccionar empresa…"}</option>
                  {companies.map((c) => (
                    <option key={c.id} value={c.id}>{c.name}</option>
                  ))}
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
                style={{ borderColor: "#DFE5ED" }}
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
                style={{ borderColor: "#DFE5ED" }}
              />
            </div>
            {(() => {
              const rolesForDropdown = isSuperAdmin ? tenantRoles : roles;
              return rolesForDropdown.length > 0 ? (
                <div>
                  <label htmlFor="invite-role" className="text-xs font-semibold block mb-1">Rol (opcional)</label>
                  <select
                    id="invite-role"
                    value={selectedRoleId}
                    onChange={(e) => setSelectedRoleId(e.target.value)}
                    className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
                    style={{ borderColor: "#DFE5ED" }}
                  >
                    <option value="">Sin rol asignado</option>
                    {rolesForDropdown.map((r) => (
                      <option key={r.id} value={r.id}>{r.name}</option>
                    ))}
                  </select>
                </div>
              ) : null;
            })()}
            {error && (
              <p role="alert" className="text-xs py-2 px-3 rounded-xl font-medium" style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}>{error}</p>
            )}
            <div className="rounded-xl p-3 border bg-[rgba(0,219,213,0.06)]" style={{ borderColor: "#DFE5ED" }}>
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
              disabled={status === "loading"}
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
