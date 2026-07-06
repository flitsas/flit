"use client";

import { useCallback, useEffect, useState } from "react";
import { Plus, Trash2, ShieldCheck, Lock } from "lucide-react";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  getRoles,
  createTenantRole,
  setTenantRolePermissions,
  deleteTenantRole,
  type TenantRole,
  type AccessibleModule,
} from "@/lib/api/security";
import { apiFetch } from "@/lib/api/client";
import { ApiError } from "@/lib/api/types";

export default function EmpresaRolesPage() {
  return (
    <ToastProvider>
      <RolesList />
    </ToastProvider>
  );
}

function RolesList() {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [roles, setRoles] = useState<TenantRole[]>([]);
  const [modules, setModules] = useState<AccessibleModule[]>([]);
  const [createOpen, setCreateOpen] = useState(false);
  const [permOpen, setPermOpen] = useState<TenantRole | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const [r, m] = await Promise.all([
        getRoles(),
        apiFetch<AccessibleModule[]>("/api/v1/security/modules"),
      ]);
      if (signal?.aborted) return;
      setRoles(r);
      setModules(m);
      setStatus(r.length === 0 ? "empty" : "ready");
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

  async function handleCreate(code: string, name: string, description: string) {
    try {
      await createTenantRole(code, name, description || undefined);
      show(`Rol «${name}» creado.`, "success");
      setCreateOpen(false);
      void load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        show("Ya existe un rol con ese código.", "error");
      } else if (err instanceof ApiError && err.status === 400) {
        show("Código de rol reservado por el sistema.", "error");
      } else {
        show("No se pudo crear el rol.", "error");
      }
    }
  }

  async function handleDelete(role: TenantRole) {
    if (!confirm(`¿Eliminar el rol «${role.name}»? Esta acción no se puede deshacer.`)) return;
    try {
      await deleteTenantRole(role.id);
      show(`Rol «${role.name}» eliminado.`, "success");
      void load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        show("El rol tiene usuarios activos o es de sistema.", "error");
      } else {
        show("No se pudo eliminar el rol.", "error");
      }
    }
  }

  async function handleSetPermissions(roleId: string, permissionIds: string[]) {
    try {
      await setTenantRolePermissions(roleId, permissionIds);
      show("Permisos actualizados.", "success");
      setPermOpen(null);
      void load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        show("Solo puedes asignar permisos que tú mismo posees.", "error");
      } else {
        show("No se pudieron actualizar los permisos.", "error");
      }
    }
  }

  return (
    <div className="max-w-5xl mx-auto space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold" style={{ color: "#162744" }}>Roles</h1>
          <p className="text-xs mt-0.5" style={{ color: "#557EFF" }}>
            Crea roles y asigna permisos para tu empresa
          </p>
        </div>
        <button
          onClick={() => setCreateOpen(true)}
          className="flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm font-medium text-white transition hover:opacity-90"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <Plus className="h-4 w-4" />
          Nuevo rol
        </button>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="No hay roles definidos para tu empresa todavía."
        emptyCta={
          <button
            onClick={() => setCreateOpen(true)}
            className="mt-3 flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm font-medium text-white"
            style={{ background: "#557EFF" }}
          >
            <Plus className="h-4 w-4" />
            Crear primer rol
          </button>
        }
        onRetry={() => void load()}
      >
        <div className="grid gap-3">
          {roles.map((role) => (
            <div
              key={role.id}
              className="flex items-center justify-between rounded-xl border px-5 py-4 bg-white transition hover:border-blue-300"
            >
              <div className="flex items-center gap-3">
                <div className="h-9 w-9 rounded-full grid place-items-center" style={{ background: "rgba(85,126,255,0.10)" }}>
                  {role.isSystem ? (
                    <Lock className="h-4 w-4" style={{ color: "#557EFF" }} />
                  ) : (
                    <ShieldCheck className="h-4 w-4" style={{ color: "#557EFF" }} />
                  )}
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <span className="font-semibold text-sm">{role.name}</span>
                    {role.isSystem && (
                      <span className="text-[10px] px-1.5 py-0.5 rounded font-medium" style={{ background: "rgba(85,126,255,0.12)", color: "#557EFF" }}>
                        sistema
                      </span>
                    )}
                  </div>
                  <div className="text-xs" style={{ color: "#9CA3AF" }}>
                    Código: <span className="font-mono">{role.code}</span>
                    {" · "}
                    {role.permissionCount} permiso{role.permissionCount !== 1 ? "s" : ""}
                  </div>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => setPermOpen(role)}
                  className="px-3 py-1.5 rounded-lg text-xs font-medium border transition hover:border-blue-400"
                  style={{ color: "#557EFF" }}
                >
                  Permisos
                </button>
                {!role.isSystem && (
                  <button
                    onClick={() => handleDelete(role)}
                    className="p-1.5 rounded-lg border transition hover:border-red-300 hover:bg-red-50"
                    aria-label="Eliminar rol"
                  >
                    <Trash2 className="h-3.5 w-3.5" style={{ color: "#FF4E00" }} />
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      </UiStateBoundary>

      {createOpen && (
        <CreateRoleDialog
          onConfirm={handleCreate}
          onClose={() => setCreateOpen(false)}
        />
      )}

      {permOpen && (
        <PermissionsDialog
          role={permOpen}
          modules={modules}
          onConfirm={(ids) => handleSetPermissions(permOpen.id, ids)}
          onClose={() => setPermOpen(null)}
        />
      )}
    </div>
  );
}

function CreateRoleDialog({
  onConfirm,
  onClose,
}: {
  onConfirm: (code: string, name: string, description: string) => void;
  onClose: () => void;
}) {
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [busy, setBusy] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    await onConfirm(code.trim().toUpperCase().replace(/\s+/g, "_"), name.trim(), description.trim());
    setBusy(false);
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" style={{ background: "rgba(22,39,68,0.45)" }}>
      <div className="w-full max-w-md rounded-2xl p-6 shadow-2xl" style={{ background: "#FFFFFF" }}>
        <h2 className="text-base font-bold mb-4" style={{ color: "#162744" }}>Crear rol</h2>
        <form onSubmit={handleSubmit} className="space-y-4">
          <Field label="Nombre del rol">
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              placeholder="Ej. Supervisor de trámites"
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-blue-400"
            />
          </Field>
          <Field label="Código único">
            <input
              value={code}
              onChange={(e) => setCode(e.target.value.toUpperCase().replace(/[^A-Z0-9_]/g, ""))}
              required
              placeholder="SUPERVISOR_TRAMITES"
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-blue-400 font-mono"
            />
            <p className="text-[10px] mt-1" style={{ color: "#9CA3AF" }}>Solo letras mayúsculas, números y guion bajo.</p>
          </Field>
          <Field label="Descripción (opcional)">
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
              placeholder="Describe brevemente el propósito del rol"
              className="w-full px-3 py-2 text-sm rounded-lg border outline-none focus:ring-2 focus:ring-blue-400 resize-none"
            />
          </Field>
          <div className="flex gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="flex-1 py-2 rounded-lg text-sm font-medium border transition hover:bg-gray-50"
              style={{ color: "#162744" }}
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={busy || !name || !code}
              className="flex-1 py-2 rounded-lg text-sm font-medium text-white transition hover:opacity-90 disabled:opacity-60"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              {busy ? "Creando…" : "Crear rol"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function PermissionsDialog({
  role,
  modules,
  onConfirm,
  onClose,
}: {
  role: TenantRole;
  modules: AccessibleModule[];
  onConfirm: (permissionIds: string[]) => void;
  onClose: () => void;
}) {
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);

  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleModule(m: AccessibleModule) {
    const allSelected = m.actions.every((a) => selected.has(a.id));
    setSelected((prev) => {
      const next = new Set(prev);
      if (allSelected) m.actions.forEach((a) => next.delete(a.id));
      else m.actions.forEach((a) => next.add(a.id));
      return next;
    });
  }

  async function handleSubmit() {
    setBusy(true);
    await onConfirm([...selected]);
    setBusy(false);
  }

  const allActions = modules.flatMap((m) => m.actions);
  const totalActions = allActions.length;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" style={{ background: "rgba(22,39,68,0.45)" }}>
      <div className="w-full max-w-lg rounded-2xl shadow-2xl flex flex-col" style={{ background: "#FFFFFF", maxHeight: "80vh" }}>
        <div className="px-6 pt-6 pb-4 border-b" style={{ borderColor: "#EEF5FF" }}>
          <h2 className="text-base font-bold" style={{ color: "#162744" }}>Permisos — {role.name}</h2>
          <p className="text-xs mt-0.5" style={{ color: "#9CA3AF" }}>
            Solo puedes asignar permisos que tú mismo posees. {selected.size}/{totalActions} seleccionados.
          </p>
        </div>

        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
          {modules.length === 0 ? (
            <p className="text-sm text-center py-8" style={{ color: "#9CA3AF" }}>No tienes permisos disponibles para asignar.</p>
          ) : (
            modules.map((m) => {
              const allSelected = m.actions.every((a) => selected.has(a.id));
              const someSelected = m.actions.some((a) => selected.has(a.id));
              return (
                <div key={m.id}>
                  <button
                    onClick={() => toggleModule(m)}
                    className="flex items-center gap-2 w-full text-left mb-2"
                  >
                    <span
                      className="h-4 w-4 rounded border grid place-items-center flex-shrink-0"
                      style={{
                        borderColor: allSelected || someSelected ? "#557EFF" : "#DFE5ED",
                        background: allSelected ? "#557EFF" : someSelected ? "rgba(85,126,255,0.15)" : "#FFFFFF",
                      }}
                    >
                      {(allSelected || someSelected) && (
                        <span className="block h-2 w-2 rounded-sm" style={{ background: allSelected ? "#FFFFFF" : "#557EFF" }} />
                      )}
                    </span>
                    <span className="text-sm font-semibold" style={{ color: "#162744" }}>{m.name}</span>
                  </button>
                  <div className="ml-6 grid grid-cols-2 gap-x-4 gap-y-1">
                    {m.actions.map((a) => (
                      <label key={a.id} className="flex items-center gap-2 cursor-pointer group">
                        <input
                          type="checkbox"
                          checked={selected.has(a.id)}
                          onChange={() => toggle(a.id)}
                          className="accent-blue-500 h-3.5 w-3.5"
                        />
                        <span className="text-xs group-hover:text-blue-600 transition" style={{ color: "#162744" }}>{a.name}</span>
                      </label>
                    ))}
                  </div>
                </div>
              );
            })
          )}
        </div>

        <div className="px-6 py-4 border-t flex gap-3" style={{ borderColor: "#EEF5FF" }}>
          <button
            onClick={onClose}
            className="flex-1 py-2 rounded-lg text-sm font-medium border transition hover:bg-gray-50"
            style={{ color: "#162744" }}
          >
            Cancelar
          </button>
          <button
            onClick={handleSubmit}
            disabled={busy}
            className="flex-1 py-2 rounded-lg text-sm font-medium text-white transition hover:opacity-90 disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {busy ? "Guardando…" : "Guardar permisos"}
          </button>
        </div>
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
