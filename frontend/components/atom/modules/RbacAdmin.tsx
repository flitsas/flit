"use client";

import { useState, useEffect } from "react";
import { Plus, ChevronDown, ChevronRight, Trash2, PowerOff, Power, Building2, Landmark } from "lucide-react";
import { superadminClient, RbacModule, RbacPermission, RbacRole, CompanyItem, TenantModuleGrantItem } from "@/lib/api/superadmin-client";
import { Modal } from "@/components/atom/Modal";
import {
  fetchTransitOfficeTenants,
  type TransitOfficeTenantItem,
} from "@/lib/api/admin-transit-office-tenants";
import { ModuleTitle } from "./ModuleTitle";

const RBAC_TABS = [
  { id: "modules", label: "Módulos y Permisos" },
  { id: "roles", label: "Roles por Tenant" },
] as const;
type RbacTabId = (typeof RBAC_TABS)[number]["id"];

export function RbacAdmin() {
  const [activeTab, setActiveTab] = useState<RbacTabId>("modules");

  // ── Módulos ──
  const [modules, setModules] = useState<RbacModule[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const [permissions, setPermissions] = useState<Record<string, RbacPermission[]>>({});
  const [showCreateModule, setShowCreateModule] = useState(false);
  const [createPermissionForModule, setCreatePermissionForModule] = useState<RbacModule | null>(null);
  const [grantsForModule, setGrantsForModule] = useState<RbacModule | null>(null);

  async function loadModules() {
    setLoading(true);
    setError(null);
    try {
      const data = await superadminClient.listModules();
      setModules(data);
    } catch {
      setError("Error al cargar módulos.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    loadModules();
  }, []);

  async function toggleExpand(mod: RbacModule) {
    const id = mod.id;
    if (expanded[id]) {
      setExpanded((e) => ({ ...e, [id]: false }));
    } else {
      if (!permissions[id]) {
        try {
          const perms = await superadminClient.listPermissions(id);
          setPermissions((p) => ({ ...p, [id]: perms }));
        } catch {
          /* silent */
        }
      }
      setExpanded((e) => ({ ...e, [id]: true }));
    }
  }

  async function handleDeactivateModule(id: string) {
    try {
      await superadminClient.deactivateModule(id);
      loadModules();
    } catch {
      /* silent */
    }
  }

  async function handleActivateModule(id: string) {
    try {
      await superadminClient.activateModule(id);
      loadModules();
    } catch {
      /* silent */
    }
  }

  async function handleDeleteModule(id: string) {
    try {
      await superadminClient.deleteModule(id);
      loadModules();
    } catch (err: unknown) {
      const body = err instanceof Error ? err.message : "";
      if (body.includes("MODULE_HAS_ACTIVE_PERMISSIONS")) {
        alert("No se puede eliminar: tiene permisos activos.");
      }
    }
  }

  return (
    <div
      className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white"
      style={{ fontFamily: "Poppins, sans-serif" }}
    >
      {/* Header — unificado con ModuleTitle (HU #10493): botón fuera de la caja del título. */}
      <div className="flex flex-col gap-3">
        <button
          onClick={() => window.history.back()}
          className="flex w-fit items-center gap-1 text-xs opacity-60 hover:opacity-100"
        >
          ← Volver
        </button>
        <ModuleTitle
          title="RBAC — Administración"
          subtitle="Gestiona módulos, permisos y roles del sistema."
          action={
            activeTab === "modules" ? (
              <button
                onClick={() => setShowCreateModule(true)}
                className="flex shrink-0 items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white"
                style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
              >
                <Plus className="h-4 w-4" /> Nuevo módulo
              </button>
            ) : undefined
          }
        />
      </div>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-[#DFE5ED]">
        {RBAC_TABS.map((t) => (
          <button
            key={t.id}
            onClick={() => setActiveTab(t.id)}
            className="relative px-4 py-2.5 text-xs font-semibold transition"
            style={{ color: activeTab === t.id ? "#557EFF" : "#162744", opacity: activeTab === t.id ? 1 : 0.6 }}
          >
            {t.label}
            {activeTab === t.id && (
              <span className="absolute bottom-0 left-0 right-0 h-0.5 rounded-full" style={{ background: "#557EFF" }} />
            )}
          </button>
        ))}
      </div>

      {/* ── Pestaña Módulos y Permisos ── */}
      {activeTab === "modules" && <div
        className="rounded-2xl bg-white dark:bg-[#0B0F14] border overflow-hidden"
      >
        <div
          className="grid px-4 py-2.5 text-[10px] font-semibold uppercase"
          style={{
            gridTemplateColumns: "40px 120px 1fr 1fr 80px 80px 90px 120px",
            background: "#DFE5ED",
            color: "#162744",
          }}
        >
          <div />
          <div>Código</div>
          <div>Nombre</div>
          <div>Descripción</div>
          <div className="text-center">Permisos</div>
          <div className="text-center">Activo</div>
          <div className="text-center">Empresas</div>
          <div className="text-right">Acciones</div>
        </div>

        {loading && (
          <div className="py-12 text-center text-sm opacity-60">Cargando módulos…</div>
        )}
        {!loading && error && (
          <div className="py-12 text-center text-sm" style={{ color: "#FF4E00" }}>
            {error}
          </div>
        )}
        {!loading && !error && modules.length === 0 && (
          <div className="py-12 text-center text-sm opacity-60">
            No hay módulos. Crea el primero.
          </div>
        )}
        {!loading &&
          !error &&
          modules.map((mod) => (
            <div key={mod.id}>
              <div
                className="grid items-center px-4 py-3 border-b text-xs"
                style={{
                  gridTemplateColumns: "40px 120px 1fr 1fr 80px 80px 90px 120px",
                  }}
              >
                <button
                  onClick={() => toggleExpand(mod)}
                  aria-label={expanded[mod.id] ? "Colapsar permisos" : "Expandir permisos"}
                  className="opacity-60 hover:opacity-100"
                >
                  {expanded[mod.id] ? (
                    <ChevronDown className="h-4 w-4" />
                  ) : (
                    <ChevronRight className="h-4 w-4" />
                  )}
                </button>
                <div className="font-mono">{mod.code}</div>
                <div className="font-semibold">{mod.name}</div>
                <div className="opacity-70">{mod.description ?? "—"}</div>
                <div className="text-center font-bold" style={{ color: "#557EFF" }}>
                  {mod.permissionCount}
                </div>
                <div className="text-center">
                  <span
                    className="px-2 py-0.5 rounded-full text-[10px] font-semibold text-white"
                    style={{ background: mod.isActive ? "#00DBD5" : "#FF4E00" }}
                  >
                    {mod.isActive ? "Activo" : "Inactivo"}
                  </span>
                </div>
                <div className="flex justify-center">
                  <button
                    onClick={() => setGrantsForModule(mod)}
                    aria-label="Gestionar empresas"
                    className="p-1.5 rounded-lg border opacity-60 hover:opacity-100"
                    style={{ color: "#557EFF" }}
                  >
                    <Building2 className="h-3.5 w-3.5" />
                  </button>
                </div>
                <div className="flex justify-end gap-1">
                  {mod.isActive ? (
                    <button
                      onClick={() => handleDeactivateModule(mod.id)}
                      aria-label="Desactivar módulo"
                      className="p-1.5 rounded-lg border opacity-60 hover:opacity-100"
                    >
                      <PowerOff className="h-3.5 w-3.5" />
                    </button>
                  ) : (
                    <button
                      onClick={() => handleActivateModule(mod.id)}
                      aria-label="Activar módulo"
                      className="p-1.5 rounded-lg border opacity-60 hover:opacity-100"
                      style={{ color: "#00DBD5" }}
                    >
                      <Power className="h-3.5 w-3.5" />
                    </button>
                  )}
                  <button
                    onClick={() => handleDeleteModule(mod.id)}
                    aria-label="Eliminar módulo"
                    className="p-1.5 rounded-lg border opacity-60 hover:opacity-100"
                    style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                  <button
                    onClick={() => setCreatePermissionForModule(mod)}
                    className="px-2 py-1 rounded-lg text-[10px] font-semibold text-white"
                    style={{ background: "#557EFF" }}
                  >
                    + Permiso
                  </button>
                </div>
              </div>
              {/* Sublistado de permisos */}
              {expanded[mod.id] && (
                <div className="border-b px-8 py-3 bg-[#F8FAFF] dark:bg-white/5">
                  {(permissions[mod.id] ?? []).length === 0 ? (
                    <p className="text-xs opacity-60">
                      Sin permisos. Haz click en &quot;+ Permiso&quot; para agregar.
                    </p>
                  ) : (
                    <div className="space-y-1">
                      {permissions[mod.id].map((p) => (
                        <div key={p.id} className="flex items-center gap-3 text-xs">
                          <span
                            className="font-mono text-[11px] px-2 py-0.5 rounded-md"
                            style={{ background: "#DFE5ED" }}
                          >
                            {p.slug}
                          </span>
                          <span className="font-medium">{p.name}</span>
                          <span className="opacity-60">{p.action}</span>
                          <span
                            className="px-2 py-0.5 rounded-full text-[9px] font-semibold text-white"
                            style={{ background: p.isActive ? "#00DBD5" : "#FF4E00" }}
                          >
                            {p.isActive ? "Activo" : "Inactivo"}
                          </span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}
            </div>
          ))}
      </div>}

      {/* ── Pestaña Roles por Tenant ── */}
      {activeTab === "roles" && <RolesTab />}

      {/* Modal crear módulo */}
      {showCreateModule && (
        <CreateModuleModal
          onClose={() => setShowCreateModule(false)}
          onCreated={() => {
            setShowCreateModule(false);
            loadModules();
          }}
        />
      )}

      {/* Modal gestión empresas (grants) */}
      {grantsForModule && (
        <ModuleGrantsModal
          module={grantsForModule}
          onClose={() => setGrantsForModule(null)}
        />
      )}

      {/* Modal crear permiso */}
      {createPermissionForModule && (
        <CreatePermissionModal
          module={createPermissionForModule}
          onClose={() => setCreatePermissionForModule(null)}
          onCreated={() => {
            const id = createPermissionForModule.id;
            superadminClient
              .listPermissions(id)
              .then((perms) => setPermissions((p) => ({ ...p, [id]: perms })))
              .catch(() => {});
            setCreatePermissionForModule(null);
            loadModules();
          }}
        />
      )}
    </div>
  );
}

function RolesTab() {
  const [companies, setCompanies] = useState<CompanyItem[]>([]);
  const [transitOfficeTenants, setTransitOfficeTenants] = useState<TransitOfficeTenantItem[]>([]);
  const [selectedTenantId, setSelectedTenantId] = useState("");
  const [roles, setRoles] = useState<RbacRole[]>([]);
  const [rolesLoading, setRolesLoading] = useState(false);
  const [rolesError, setRolesError] = useState<string | null>(null);
  const [showCreateRole, setShowCreateRole] = useState(false);

  useEffect(() => {
    superadminClient.listCompanies()
      .then((r) => setCompanies(r.data))
      .catch(() => {});
    // Tenants OT (refactor adminOT): el SuperAdmin también puede curar los permisos
    // del rol ot_admin de cada organismo de tránsito, no solo de compañías.
    fetchTransitOfficeTenants({ pageSize: 100 })
      .then((r) => setTransitOfficeTenants(r.data))
      .catch(() => {});
  }, []);

  useEffect(() => {
    if (!selectedTenantId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setRoles([]);
      return;
    }
    setRolesLoading(true);
    setRolesError(null);
    superadminClient.listRoles(selectedTenantId)
      .then(setRoles)
      .catch(() => setRolesError("Error al cargar roles."))
      .finally(() => setRolesLoading(false));
  }, [selectedTenantId]);

  async function handleDeleteRole(id: string) {
    try {
      await superadminClient.deleteRole(id);
      setRoles((r) => r.filter((x) => x.id !== id));
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "";
      if (msg.includes("ROLE_SYSTEM_LOCKED")) alert("No se puede eliminar: es un rol de sistema.");
      else if (msg.includes("ROLE_HAS_ACTIVE_USERS")) alert("No se puede eliminar: tiene usuarios activos asignados.");
      else alert("Error al eliminar el rol.");
    }
  }

  return (
    <div className="flex flex-col gap-4">
      {/* Selector de tenant: compañías y tenants OT (refactor adminOT) en un mismo picker */}
      <div className="flex items-center gap-3">
        <label htmlFor="tenant-picker" className="text-sm font-semibold shrink-0">Tenant:</label>
        <select
          id="tenant-picker"
          value={selectedTenantId}
          onChange={(e) => setSelectedTenantId(e.target.value)}
          className="text-sm px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] dark:text-white outline-none focus:border-[#557EFF]"
          style={{ minWidth: 280 }}
        >
          <option value="">— Selecciona un tenant —</option>
          {companies.length > 0 && (
            <optgroup label="Compañías">
              {companies.map((c) => (
                <option key={c.id} value={c.id}>{c.razonSocial} ({c.nit})</option>
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
        {selectedTenantId && transitOfficeTenants.some((t) => t.id === selectedTenantId) && (
          <span className="flex items-center gap-1.5 text-xs font-medium" style={{ color: "#557EFF" }}>
            <Landmark className="h-3.5 w-3.5" aria-hidden="true" />
            Organismo de Tránsito
          </span>
        )}
        {selectedTenantId && (
          <button
            onClick={() => setShowCreateRole(true)}
            className="flex items-center gap-2 px-4 py-2 rounded-xl text-sm font-semibold text-white"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            <Plus className="h-4 w-4" /> Nuevo rol
          </button>
        )}
      </div>

      {/* Tabla de roles */}
      {!selectedTenantId && (
        <div className="py-12 text-center text-sm opacity-60">Selecciona un tenant para ver sus roles.</div>
      )}
      {selectedTenantId && (
        <div className="rounded-2xl bg-white dark:bg-[#0B0F14] border overflow-hidden">
          <div className="grid px-4 py-2.5 text-[10px] font-semibold uppercase" style={{ gridTemplateColumns: "120px 1fr 1fr 80px 80px 80px", background: "#DFE5ED", color: "#162744" }}>
            <div>Código</div>
            <div>Nombre</div>
            <div>Descripción</div>
            <div className="text-center">Permisos</div>
            <div className="text-center">Sistema</div>
            <div className="text-right">Acciones</div>
          </div>
          {rolesLoading && <div className="py-12 text-center text-sm opacity-60">Cargando roles…</div>}
          {!rolesLoading && rolesError && <div role="alert" className="py-12 text-center text-sm" style={{ color: "#FF4E00" }}>{rolesError}</div>}
          {!rolesLoading && !rolesError && roles.length === 0 && (
            <div className="py-12 text-center text-sm opacity-60">No hay roles. Crea el primero.</div>
          )}
          {!rolesLoading && !rolesError && roles.map((r) => (
            <div key={r.id} className="grid items-center px-4 py-3 border-b text-xs" style={{ gridTemplateColumns: "120px 1fr 1fr 80px 80px 80px" }}>
              <div className="font-mono opacity-80">{r.code}</div>
              <div className="font-semibold">{r.name}</div>
              <div className="opacity-70">{r.description ?? "—"}</div>
              <div className="text-center font-bold" style={{ color: "#557EFF" }}>{r.permissionCount}</div>
              <div className="text-center">
                {r.isSystem ? (
                  <span className="px-2 py-0.5 rounded-full text-[9px] font-semibold text-white" style={{ background: "#557EFF" }}>Sistema</span>
                ) : (
                  <span className="opacity-40">—</span>
                )}
              </div>
              <div className="flex justify-end">
                {!r.isSystem && (
                  <button
                    onClick={() => handleDeleteRole(r.id)}
                    aria-label={`Eliminar rol ${r.name}`}
                    className="p-1.5 rounded-lg transition hover:opacity-80"
                    style={{ color: "#FF4E00" }}
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {showCreateRole && selectedTenantId && (
        <CreateRoleModal
          tenantId={selectedTenantId}
          onClose={() => setShowCreateRole(false)}
          onCreated={() => {
            setShowCreateRole(false);
            superadminClient.listRoles(selectedTenantId).then(setRoles).catch(() => {});
          }}
        />
      )}
    </div>
  );
}

function CreateRoleModal({
  tenantId, onClose, onCreated,
}: {
  tenantId: string;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await superadminClient.createRole({ tenantId, code: code.trim(), name: name.trim(), description: description.trim() || undefined });
      onCreated();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "";
      setError(msg.includes("ROLE_CODE_DUPLICATE") ? "Este código ya existe en el tenant." : "Error al crear el rol.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal open onClose={onClose} busy={busy} size="sm" title="Nuevo rol" titleClassName="text-lg font-bold text-[#162744] dark:text-white">
        <form onSubmit={handleSubmit} className="space-y-3">
          <div>
            <label htmlFor="role-code" className="text-xs font-semibold block mb-1">Código *</label>
            <input id="role-code" required value={code} onChange={(e) => setCode(e.target.value)} placeholder="admin_tenant" className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]" />
          </div>
          <div>
            <label htmlFor="role-name" className="text-xs font-semibold block mb-1">Nombre *</label>
            <input id="role-name" required value={name} onChange={(e) => setName(e.target.value)} placeholder="Administrador Tenant" className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]" />
          </div>
          <div>
            <label htmlFor="role-desc" className="text-xs font-semibold block mb-1">Descripción</label>
            <input id="role-desc" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Acceso completo a la configuración del tenant" className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]" />
          </div>
          {error && <p role="alert" className="text-xs py-2 px-3 rounded-xl font-medium" style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}>{error}</p>}
          <button type="submit" disabled={busy} className="w-full py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-60" style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}>
            {busy ? "Creando…" : "Crear rol"}
          </button>
        </form>
    </Modal>
  );
}

function CreateModuleModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: () => void;
}) {
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [sortOrder, setSortOrder] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      await superadminClient.createModule({
        code: code.trim(),
        name: name.trim(),
        description: description.trim() || undefined,
        sortOrder: sortOrder ? parseInt(sortOrder) : undefined,
      });
      onCreated();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "";
      if (msg.includes("MODULE_CODE_DUPLICATE")) setError("Este código ya existe.");
      else setError("No se pudo crear el módulo.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <Modal open onClose={onClose} busy={loading} size="sm" title="Nuevo módulo" titleClassName="text-lg font-bold text-[#162744] dark:text-white">
        <form onSubmit={handleSubmit} className="space-y-3">
          <div>
            <label htmlFor="mod-code" className="text-xs font-semibold block mb-1">
              Código *
            </label>
            <input
              id="mod-code"
              required
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="tramites"
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
              style={{ borderColor: error?.includes("código") ? "#FF4E00" : "#DFE5ED" }}
            />
            {error?.includes("código") && (
              <p className="text-[11px] mt-1" style={{ color: "#FF4E00" }}>
                {error}
              </p>
            )}
          </div>
          <div>
            <label htmlFor="mod-name" className="text-xs font-semibold block mb-1">
              Nombre *
            </label>
            <input
              id="mod-name"
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Trámites"
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
            />
          </div>
          <div>
            <label htmlFor="mod-desc" className="text-xs font-semibold block mb-1">
              Descripción
            </label>
            <input
              id="mod-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Gestión de trámites vehiculares"
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
            />
          </div>
          <div>
            <label htmlFor="mod-sort" className="text-xs font-semibold block mb-1">
              Orden (sort)
            </label>
            <input
              id="mod-sort"
              type="number"
              value={sortOrder}
              onChange={(e) => setSortOrder(e.target.value)}
              placeholder="1"
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
            />
          </div>
          {error && !error.includes("código") && (
            <p
              role="alert"
              className="text-xs px-3 py-2 rounded-xl"
              style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}
            >
              {error}
            </p>
          )}
          <button
            type="submit"
            disabled={loading}
            className="w-full py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {loading ? "Creando…" : "Crear módulo"}
          </button>
        </form>
    </Modal>
  );
}

function ModuleGrantsModal({
  module,
  onClose,
}: {
  module: RbacModule;
  onClose: () => void;
}) {
  const [companies, setCompanies] = useState<CompanyItem[]>([]);
  const [grants, setGrants] = useState<TenantModuleGrantItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<Record<string, boolean>>({});

  useEffect(() => {
    let active = true;
    async function load() {
      setLoading(true);
      try {
        const [c, g] = await Promise.all([
          superadminClient.listCompanies().then((r) => r.data),
          superadminClient.listModuleGrants(module.id),
        ]);
        if (active) { setCompanies(c); setGrants(g); }
      } catch {
        // ignore
      } finally {
        if (active) setLoading(false);
      }
    }
    load();
    return () => { active = false; };
  }, [module.id]);

  const grantedIds = new Set(grants.map((g) => g.tenantId));

  async function handleToggle(tenantId: string, isGranted: boolean) {
    setBusy((b) => ({ ...b, [tenantId]: true }));
    try {
      if (isGranted) {
        await superadminClient.revokeModuleFromTenant(module.id, tenantId);
        setGrants((g) => g.filter((x) => x.tenantId !== tenantId));
      } else {
        await superadminClient.grantModuleToTenant(module.id, tenantId);
        const company = companies.find((c) => c.id === tenantId);
        if (company) setGrants((g) => [...g, { tenantId, tenantName: company.razonSocial }]);
      }
    } catch {
      /* silent */
    } finally {
      setBusy((b) => ({ ...b, [tenantId]: false }));
    }
  }

  return (
    <Modal open onClose={onClose} size="sm" title="Empresas con acceso" titleClassName="text-lg font-bold text-[#162744] dark:text-white">
        <p className="text-xs opacity-60 mb-4">
          Módulo: <strong>{module.name}</strong> ({module.code})
        </p>
        {loading ? (
          <div className="py-8 text-center text-sm opacity-60">Cargando empresas…</div>
        ) : companies.length === 0 ? (
          <div className="py-8 text-center text-sm opacity-60">No hay empresas registradas.</div>
        ) : (
          <div className="space-y-2 max-h-72 overflow-y-auto">
            {companies.map((c) => {
              const granted = grantedIds.has(c.id);
              return (
                <label
                  key={c.id}
                  className="flex items-center gap-3 p-3 rounded-xl cursor-pointer hover:bg-[#F8FAFF] border"
                  style={{ borderColor: granted ? "#00DBD5" : "#DFE5ED" }}
                >
                  <input
                    type="checkbox"
                    checked={granted}
                    disabled={busy[c.id]}
                    onChange={() => handleToggle(c.id, granted)}
                    className="h-4 w-4 accent-[#557EFF]"
                  />
                  <div className="min-w-0">
                    <p className="text-sm font-semibold truncate">{c.razonSocial}</p>
                    <p className="text-[11px] opacity-60">{c.nit}</p>
                  </div>
                  {busy[c.id] && <span className="ml-auto text-[10px] opacity-50">…</span>}
                </label>
              );
            })}
          </div>
        )}
        <button
          onClick={onClose}
          className="mt-4 w-full py-2.5 rounded-xl text-sm font-semibold text-white"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          Listo
        </button>
    </Modal>
  );
}

function CreatePermissionModal({
  module,
  onClose,
  onCreated,
}: {
  module: RbacModule;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [slug, setSlug] = useState("");
  const [name, setName] = useState("");
  const [action, setAction] = useState("CUSTOM");
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      await superadminClient.createPermission({
        moduleId: module.id,
        slug: slug.trim(),
        name: name.trim(),
        action,
        description: description.trim() || undefined,
      });
      onCreated();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "";
      if (msg.includes("PERMISSION_SLUG_DUPLICATE")) setError("Este slug ya existe.");
      else if (msg.includes("MODULE_INACTIVE"))
        setError("El módulo está inactivo. Actívalo primero.");
      else setError("No se pudo crear el permiso.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <Modal open onClose={onClose} busy={loading} size="sm" title="Nuevo permiso" titleClassName="text-lg font-bold text-[#162744] dark:text-white">
        <p className="text-xs opacity-60 mb-4">
          Módulo: <strong>{module.name}</strong> ({module.code})
        </p>
        <form onSubmit={handleSubmit} className="space-y-3">
          <div>
            <label htmlFor="perm-slug" className="text-xs font-semibold block mb-1">
              Slug *
            </label>
            <input
              id="perm-slug"
              required
              value={slug}
              onChange={(e) => setSlug(e.target.value)}
              placeholder="tramites.read"
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF] font-mono"
            />
          </div>
          <div>
            <label htmlFor="perm-name" className="text-xs font-semibold block mb-1">
              Nombre *
            </label>
            <input
              id="perm-name"
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Leer trámites"
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
            />
          </div>
          <div>
            <label htmlFor="perm-action" className="text-xs font-semibold block mb-1">
              Acción
            </label>
            <select
              id="perm-action"
              value={action}
              onChange={(e) => setAction(e.target.value)}
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none"
            >
              {["CUSTOM", "READ", "CREATE", "UPDATE", "DELETE"].map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="perm-desc" className="text-xs font-semibold block mb-1">
              Descripción
            </label>
            <input
              id="perm-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Permite leer el listado de trámites"
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
            />
          </div>
          {error && (
            <p
              role="alert"
              className="text-xs px-3 py-2 rounded-xl"
              style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}
            >
              {error}
            </p>
          )}
          <button
            type="submit"
            disabled={loading}
            className="w-full py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {loading ? "Creando…" : "Crear permiso"}
          </button>
        </form>
    </Modal>
  );
}
