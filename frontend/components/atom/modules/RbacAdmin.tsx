"use client";

import { useState, useEffect, useCallback } from "react";
import { ChevronDown, ChevronRight, Trash2, PowerOff, Power, Building2, Landmark, Pencil, AlertTriangle } from "lucide-react";
import {
  superadminClient,
  RbacModule,
  RbacPermission,
  RbacRole,
  RbacRoleDetail,
  RoleTargetEntityType,
  CompanyItem,
  TenantModuleGrantItem,
} from "@/lib/api/superadmin-client";
import { getAccessibleModules, type AccessibleModule } from "@/lib/api/security";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import { ModuleTitle } from "./ModuleTitle";

const RBAC_TABS = [
  { id: "modules", label: "Módulos y Permisos" },
  { id: "roles", label: "Roles del sistema" },
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
                Nuevo módulo
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

      {/* ── Pestaña Roles del sistema (HU #10509) ── */}
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

// HU #10509 — el catálogo de roles es GLOBAL por tipo de entidad (HU #10505); ya no se filtra
// por tenant. Se muestran SIEMPRE ambas tablas (AC2) y la creación/edición vive en modales.
const ENTITY_TABLES: { type: RoleTargetEntityType; title: string; icon: typeof Building2 }[] = [
  { type: "COMPANY", title: "Roles de Compañía", icon: Building2 },
  { type: "TRANSIT_OFFICE", title: "Roles de Organismo de Tránsito", icon: Landmark },
];

/** Rol enriquecido en cliente con el estado activo/inactivo (HU #10509 nota de diseño: el
 * catálogo GLOBAL no expone `isActive` en el listado — RoleSummary del backend solo trae
 * id/code/name/description/isSystem/permissionCount/createdAt. Mientras no exista ese campo
 * o un GET por id, se asume `true` al cargar y se corrige localmente tras Activar/Desactivar
 * en la misma sesión. Ver nota en el resumen de la HU. */
interface RoleRow extends RbacRole {
  isActive: boolean;
}

function RolesTab() {
  return (
    <ToastProvider>
      <RolesTabContent />
    </ToastProvider>
  );
}

function RolesTabContent() {
  const { show } = useToast();
  const [companyRoles, setCompanyRoles] = useState<RoleRow[]>([]);
  const [otRoles, setOtRoles] = useState<RoleRow[]>([]);
  const [companyStatus, setCompanyStatus] = useState<"loading" | "error" | "ready">("loading");
  const [otStatus, setOtStatus] = useState<"loading" | "error" | "ready">("loading");
  const [busyIds, setBusyIds] = useState<Record<string, boolean>>({});
  const [showCreateRole, setShowCreateRole] = useState(false);
  const [editTarget, setEditTarget] = useState<{ role: RoleRow; targetEntityType: RoleTargetEntityType } | null>(null);
  // Caché en sesión de permisos por rol (id → permisos completos), poblada al crear o editar
  // un rol. Necesaria porque el backend no expone un GET por id (ver nota arriba).
  const [permissionsCache, setPermissionsCache] = useState<Record<string, RbacRoleDetail["permissions"]>>({});

  const loadRoles = useCallback((type: RoleTargetEntityType) => {
    const setStatus = type === "COMPANY" ? setCompanyStatus : setOtStatus;
    const setRoles = type === "COMPANY" ? setCompanyRoles : setOtRoles;
    setStatus("loading");
    superadminClient
      .listRoles(type)
      .then((data) => {
        setRoles((prev) => {
          const prevActive = new Map(prev.map((r) => [r.id, r.isActive]));
          return data.map((r) => ({ ...r, isActive: prevActive.get(r.id) ?? true }));
        });
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    loadRoles("COMPANY");
    loadRoles("TRANSIT_OFFICE");
  }, [loadRoles]);

  function cachePermissions(roleId: string, permissions: RbacRoleDetail["permissions"]) {
    setPermissionsCache((c) => ({ ...c, [roleId]: permissions }));
  }

  async function handleToggleActive(role: RoleRow, targetEntityType: RoleTargetEntityType) {
    setBusyIds((b) => ({ ...b, [role.id]: true }));
    try {
      if (role.isActive) {
        await superadminClient.deactivateRole(role.id);
        show(`Rol «${role.name}» desactivado.`, "success");
      } else {
        await superadminClient.activateRole(role.id);
        show(`Rol «${role.name}» activado.`, "success");
      }
      const setRoles = targetEntityType === "COMPANY" ? setCompanyRoles : setOtRoles;
      setRoles((rows) => rows.map((r) => (r.id === role.id ? { ...r, isActive: !r.isActive } : r)));
    } catch {
      show("No se pudo cambiar el estado del rol.", "error");
    } finally {
      setBusyIds((b) => ({ ...b, [role.id]: false }));
    }
  }

  async function handleDelete(role: RoleRow, targetEntityType: RoleTargetEntityType) {
    if (!confirm(`¿Eliminar el rol «${role.name}»? Esta acción no se puede deshacer.`)) return;
    setBusyIds((b) => ({ ...b, [role.id]: true }));
    try {
      await superadminClient.deleteRole(role.id);
      show(`Rol «${role.name}» eliminado.`, "success");
      const setRoles = targetEntityType === "COMPANY" ? setCompanyRoles : setOtRoles;
      setRoles((rows) => rows.filter((r) => r.id !== role.id));
    } catch (err: unknown) {
      // AC3 — el backend rechaza el borrado con 409 { code: "ROLE_HAS_ACTIVE_USERS" } cuando
      // el rol tiene usuarios asignados (DeleteRoleHandler / RoleHasActiveUsersException).
      const msg = err instanceof Error ? err.message : "";
      if (msg.includes("ROLE_HAS_ACTIVE_USERS")) {
        show("Este rol tiene usuarios asignados y no puede eliminarse.", "error");
      } else if (msg.includes("ROLE_SYSTEM_LOCKED")) {
        show("No se puede eliminar: es un rol de sistema.", "error");
      } else {
        show("No se pudo eliminar el rol.", "error");
      }
    } finally {
      setBusyIds((b) => ({ ...b, [role.id]: false }));
    }
  }

  return (
    <div className="flex flex-col gap-5">
      <div className="flex justify-end">
        <button
          onClick={() => setShowCreateRole(true)}
          className="flex items-center gap-2 px-4 py-2 rounded-xl text-sm font-semibold text-white"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          Nuevo rol
        </button>
      </div>

      {ENTITY_TABLES.map(({ type, title, icon: Icon }) => {
        const roles = type === "COMPANY" ? companyRoles : otRoles;
        const status = type === "COMPANY" ? companyStatus : otStatus;
        return (
          <section key={type} className="flex flex-col gap-2">
            <h3 className="flex items-center gap-2 text-sm font-bold">
              <Icon className="h-4 w-4" style={{ color: "#557EFF" }} aria-hidden="true" />
              {title}
            </h3>
            <div className="rounded-2xl bg-white dark:bg-[#0B0F14] border overflow-hidden">
              <div
                className="grid px-4 py-2.5 text-[10px] font-semibold uppercase"
                style={{ gridTemplateColumns: "1fr 1fr 90px 90px 110px", background: "#DFE5ED", color: "#162744" }}
              >
                <div>Nombre</div>
                <div>Descripción</div>
                <div className="text-center">Permisos</div>
                <div className="text-center">Estado</div>
                <div className="text-right">Acciones</div>
              </div>
              {status === "loading" && (
                <div className="py-10 text-center text-sm opacity-60">Cargando roles…</div>
              )}
              {status === "error" && (
                <div role="alert" className="py-10 text-center text-sm" style={{ color: "#FF4E00" }}>
                  Error al cargar roles.
                </div>
              )}
              {status === "ready" && roles.length === 0 && (
                <div className="py-10 text-center text-sm opacity-60">
                  No hay roles {type === "COMPANY" ? "de compañía" : "de organismo de tránsito"}. Crea el primero.
                </div>
              )}
              {status === "ready" &&
                roles.map((r) => {
                  const cached = permissionsCache[r.id];
                  return (
                    <div
                      key={r.id}
                      className="grid items-center px-4 py-3 border-b last:border-b-0 text-xs"
                      style={{ gridTemplateColumns: "1fr 1fr 90px 90px 110px" }}
                    >
                      <div className="min-w-0">
                        <p className="font-semibold truncate">{r.name}</p>
                        <p className="font-mono text-[10px] opacity-60">{r.code}</p>
                      </div>
                      <div className="opacity-70 truncate">{r.description ?? "—"}</div>
                      <div
                        className="text-center font-bold"
                        style={{ color: "#557EFF" }}
                        title={cached ? cached.map((p) => p.name).join(", ") || "Sin permisos" : undefined}
                      >
                        {r.permissionCount}
                      </div>
                      <div className="flex justify-center">
                        <StatusBadge
                          label={r.isActive ? "Activo" : "Inactivo"}
                          bg={r.isActive ? "rgba(0,219,213,0.15)" : "rgba(255,78,0,0.10)"}
                          color={r.isActive ? "#0f766e" : "#c2410c"}
                          border={r.isActive ? "rgba(0,219,213,0.35)" : "rgba(255,78,0,0.3)"}
                        />
                      </div>
                      <div className="flex justify-end gap-1">
                        <button
                          onClick={() => setEditTarget({ role: r, targetEntityType: type })}
                          aria-label={`Editar permisos de ${r.name}`}
                          disabled={busyIds[r.id]}
                          className="p-1.5 rounded-lg border opacity-60 hover:opacity-100 disabled:opacity-30"
                          style={{ color: "#557EFF" }}
                        >
                          <Pencil className="h-3.5 w-3.5" />
                        </button>
                        <button
                          onClick={() => handleToggleActive(r, type)}
                          aria-label={r.isActive ? `Desactivar rol ${r.name}` : `Activar rol ${r.name}`}
                          disabled={busyIds[r.id]}
                          className="p-1.5 rounded-lg border opacity-60 hover:opacity-100 disabled:opacity-30"
                          style={{ color: r.isActive ? undefined : "#00DBD5" }}
                        >
                          {r.isActive ? <PowerOff className="h-3.5 w-3.5" /> : <Power className="h-3.5 w-3.5" />}
                        </button>
                        <button
                          onClick={() => handleDelete(r, type)}
                          aria-label={`Eliminar rol ${r.name}`}
                          disabled={busyIds[r.id] || r.isSystem}
                          title={r.isSystem ? "No se puede eliminar: es un rol de sistema" : undefined}
                          className="p-1.5 rounded-lg border opacity-60 hover:opacity-100 disabled:opacity-30"
                          style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      </div>
                    </div>
                  );
                })}
            </div>
          </section>
        );
      })}

      {showCreateRole && (
        <CreateRoleModal
          onClose={() => setShowCreateRole(false)}
          onCreated={(id, targetEntityType, permissions) => {
            setShowCreateRole(false);
            cachePermissions(id, permissions);
            loadRoles(targetEntityType);
          }}
        />
      )}

      {editTarget && (
        <EditRolePermissionsModal
          role={editTarget.role}
          cachedPermissions={permissionsCache[editTarget.role.id] ?? null}
          onClose={() => setEditTarget(null)}
          onSaved={(permissions) => {
            cachePermissions(editTarget.role.id, permissions);
            loadRoles(editTarget.targetEntityType);
            setEditTarget(null);
            show(`Permisos de «${editTarget.role.name}» actualizados.`, "success");
          }}
        />
      )}
    </div>
  );
}

/** Checklist de permisos por módulo (mismo patrón visual de `/empresa/roles` — HU #10509 lo
 * trae a RbacAdmin.tsx, único lugar con CRUD de roles tras HU #10508). */
function ModulePermissionsChecklist({
  modules, modulesLoading, selected, onToggle, onToggleModule,
}: {
  modules: AccessibleModule[];
  modulesLoading: boolean;
  selected: Set<string>;
  onToggle: (id: string) => void;
  onToggleModule: (m: AccessibleModule) => void;
}) {
  if (modulesLoading) {
    return <p className="text-xs text-center py-6 opacity-60">Cargando módulos…</p>;
  }
  if (modules.length === 0) {
    return <p className="text-xs text-center py-6 opacity-60">No hay módulos con permisos disponibles.</p>;
  }
  return (
    <div className="space-y-4 max-h-64 overflow-y-auto pr-1">
      {modules.map((m) => {
        const allSelected = m.actions.length > 0 && m.actions.every((a) => selected.has(a.id));
        const someSelected = m.actions.some((a) => selected.has(a.id));
        return (
          <div key={m.id}>
            <button
              type="button"
              onClick={() => onToggleModule(m)}
              className="flex items-center gap-2 w-full text-left mb-1.5"
            >
              <span
                className="h-4 w-4 rounded border grid place-items-center flex-shrink-0"
                style={{
                  borderColor: allSelected || someSelected ? "#557EFF" : "#DFE5ED",
                  background: allSelected ? "#557EFF" : someSelected ? "rgba(85,126,255,0.15)" : "transparent",
                }}
              >
                {(allSelected || someSelected) && (
                  <span className="block h-2 w-2 rounded-sm" style={{ background: allSelected ? "#FFFFFF" : "#557EFF" }} />
                )}
              </span>
              <span className="text-sm font-semibold">{m.name}</span>
            </button>
            <div className="ml-6 grid grid-cols-2 gap-x-4 gap-y-1">
              {m.actions.map((a) => (
                <label key={a.id} className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={selected.has(a.id)}
                    onChange={() => onToggle(a.id)}
                    className="h-3.5 w-3.5 accent-[#557EFF]"
                  />
                  <span className="text-xs">{a.name}</span>
                </label>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
}

function useModulesCatalog() {
  const [modules, setModules] = useState<AccessibleModule[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    getAccessibleModules()
      .then((m) => { if (active) setModules(m); })
      .catch(() => {})
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  return { modules, loading };
}

function CreateRoleModal({
  onClose, onCreated,
}: {
  onClose: () => void;
  onCreated: (roleId: string, targetEntityType: RoleTargetEntityType, permissions: RbacRoleDetail["permissions"]) => void;
}) {
  const { modules, loading: modulesLoading } = useModulesCatalog();
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [targetEntityType, setTargetEntityType] = useState<RoleTargetEntityType>("COMPANY");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const { id } = await superadminClient.createRole({
        targetEntityType,
        code: code.trim(),
        name: name.trim(),
        description: description.trim() || undefined,
      });
      let permissions: RbacRoleDetail["permissions"] = [];
      if (selected.size > 0) {
        const detail = await superadminClient.setRolePermissions(id, [...selected]);
        permissions = detail.permissions;
      }
      onCreated(id, targetEntityType, permissions);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "";
      setError(msg.includes("ROLE_CODE_DUPLICATE") ? "Este código ya existe para este tipo de entidad." : "Error al crear el rol.");
      setBusy(false);
    }
  }

  return (
    <Modal open onClose={onClose} busy={busy} size="md" title="Nuevo rol" titleClassName="text-lg font-bold text-[#162744] dark:text-white">
      <form onSubmit={handleSubmit} className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label htmlFor="role-code" className="text-xs font-semibold block mb-1">Código *</label>
            <input id="role-code" required value={code} onChange={(e) => setCode(e.target.value)} placeholder="supervisor_tramites" className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]" />
          </div>
          <div>
            <label htmlFor="role-target-type" className="text-xs font-semibold block mb-1">Tipo de entidad *</label>
            <select
              id="role-target-type"
              required
              value={targetEntityType}
              onChange={(e) => setTargetEntityType(e.target.value as RoleTargetEntityType)}
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
            >
              <option value="COMPANY">Compañía</option>
              <option value="TRANSIT_OFFICE">Organismo de Tránsito</option>
            </select>
          </div>
        </div>
        <div>
          <label htmlFor="role-name" className="text-xs font-semibold block mb-1">Nombre *</label>
          <input id="role-name" required value={name} onChange={(e) => setName(e.target.value)} placeholder="Supervisor de trámites" className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]" />
        </div>
        <div>
          <label htmlFor="role-desc" className="text-xs font-semibold block mb-1">Descripción</label>
          <input id="role-desc" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Describe brevemente el propósito del rol" className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]" />
        </div>
        <div>
          <span className="text-xs font-semibold block mb-1.5">Permisos por módulo</span>
          <ModulePermissionsChecklist
            modules={modules}
            modulesLoading={modulesLoading}
            selected={selected}
            onToggle={toggle}
            onToggleModule={toggleModule}
          />
        </div>
        {error && <p role="alert" className="text-xs py-2 px-3 rounded-xl font-medium" style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}>{error}</p>}
        <button type="submit" disabled={busy} className="w-full py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-60" style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}>
          {busy ? "Creando…" : "Crear rol"}
        </button>
      </form>
    </Modal>
  );
}

function EditRolePermissionsModal({
  role, cachedPermissions, onClose, onSaved,
}: {
  role: RoleRow;
  /** Permisos actuales del rol si se conocen (creado o editado antes en esta sesión). El
   * backend no expone un GET por id para recuperarlos de otra forma (ver nota de diseño). */
  cachedPermissions: RbacRoleDetail["permissions"] | null;
  onClose: () => void;
  onSaved: (permissions: RbacRoleDetail["permissions"]) => void;
}) {
  const { modules, loading: modulesLoading } = useModulesCatalog();
  const [selected, setSelected] = useState<Set<string>>(
    () => new Set((cachedPermissions ?? []).map((p) => p.id)),
  );
  const [acknowledged, setAcknowledged] = useState(cachedPermissions !== null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const detail = await superadminClient.setRolePermissions(role.id, [...selected]);
      onSaved(detail.permissions);
    } catch {
      setError("No se pudieron actualizar los permisos.");
      setBusy(false);
    }
  }

  return (
    <Modal open onClose={onClose} busy={busy} size="md" title={`Editar permisos — ${role.name}`} titleClassName="text-lg font-bold text-[#162744] dark:text-white">
      <form onSubmit={handleSubmit} className="space-y-3">
        {cachedPermissions === null && (
          <div
            role="alert"
            className="flex items-start gap-2 text-xs px-3 py-2.5 rounded-xl font-medium"
            style={{ background: "rgba(245,158,11,0.12)", color: "#b45309" }}
          >
            <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" aria-hidden="true" />
            <span>
              No pudimos recuperar los permisos actuales de este rol. Selecciona <strong>todos</strong> los
              permisos que debe tener antes de guardar — al confirmar se reemplaza la lista completa.
            </span>
          </div>
        )}
        <ModulePermissionsChecklist
          modules={modules}
          modulesLoading={modulesLoading}
          selected={selected}
          onToggle={toggle}
          onToggleModule={toggleModule}
        />
        {cachedPermissions === null && (
          <label className="flex items-start gap-2 text-xs cursor-pointer">
            <input
              type="checkbox"
              checked={acknowledged}
              onChange={(e) => setAcknowledged(e.target.checked)}
              className="h-3.5 w-3.5 mt-0.5 accent-[#557EFF]"
            />
            <span>Confirmo que seleccioné todos los permisos que este rol debe tener.</span>
          </label>
        )}
        {error && <p role="alert" className="text-xs py-2 px-3 rounded-xl font-medium" style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}>{error}</p>}
        <button
          type="submit"
          disabled={busy || !acknowledged}
          className="w-full py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-60"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          {busy ? "Guardando…" : "Guardar permisos"}
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
