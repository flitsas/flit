"use client";

import { useState, useEffect, useCallback } from "react";
import { ChevronDown, ChevronRight, Trash2, PowerOff, Power, Building2, Landmark, Pencil } from "lucide-react";
import {
  superadminClient,
  RbacModule,
  RbacPermission,
  RbacRole,
  RbacRoleDetail,
  RoleTargetEntityType,
} from "@/lib/api/superadmin-client";
import { getAccessibleModules, type AccessibleModule } from "@/lib/api/security";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { activeTone } from "@/components/atom/statusTones";
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

  const moduleColumns: DataTableColumn<RbacModule>[] = [
    {
      key: "expand",
      header: "",
      render: (mod) => (
        <button
          onClick={() => toggleExpand(mod)}
          aria-label={expanded[mod.id] ? "Colapsar permisos" : "Expandir permisos"}
          className="opacity-60 hover:opacity-100"
        >
          {expanded[mod.id] ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        </button>
      ),
    },
    { key: "code", header: "Código", render: (mod) => <span className="font-mono">{mod.code}</span> },
    { key: "name", header: "Nombre", render: (mod) => <span className="font-semibold">{mod.name}</span> },
    { key: "desc", header: "Descripción", render: (mod) => <span className="opacity-70">{mod.description ?? "—"}</span> },
    {
      key: "perms",
      header: "Permisos",
      align: "center",
      render: (mod) => (
        <span className="font-bold" style={{ color: "#557EFF" }}>{mod.permissionCount}</span>
      ),
    },
    {
      key: "active",
      header: "Activo",
      align: "center",
      render: (mod) => <StatusBadge tone={activeTone(mod.isActive)} label={mod.isActive ? "Activo" : "Inactivo"} />,
    },
    {
      key: "actions",
      header: "Acciones",
      align: "right",
      render: (mod) => (
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
      ),
    },
  ];

  const renderModulePermissions = (mod: RbacModule) => {
    if (!expanded[mod.id]) return null;
    const perms = permissions[mod.id] ?? [];
    return (
      <div className="rounded-xl px-4 py-3 bg-[#F8FAFF] dark:bg-white/5">
        {perms.length === 0 ? (
          <p className="text-xs opacity-60">Sin permisos. Haz click en &quot;+ Permiso&quot; para agregar.</p>
        ) : (
          <div className="space-y-1">
            {perms.map((p) => (
              <div key={p.id} className="flex items-center gap-3 text-xs">
                <span className="font-mono text-[11px] px-2 py-0.5 rounded-md" style={{ background: "#DFE5ED" }}>
                  {p.slug}
                </span>
                <span className="font-medium">{p.name}</span>
                <span className="opacity-60">{p.action}</span>
                <StatusBadge tone={activeTone(p.isActive)} label={p.isActive ? "Activo" : "Inactivo"} />
              </div>
            ))}
          </div>
        )}
      </div>
    );
  };

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
      {activeTab === "modules" && (
        <DataTable
          columns={moduleColumns}
          rows={modules}
          getRowKey={(m) => m.id}
          status={loading ? "loading" : error ? "error" : "ready"}
          errorMessage={error ?? undefined}
          emptyMessage="No hay módulos. Crea el primero."
          minWidth={760}
          renderExpanded={renderModulePermissions}
        />
      )}

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

function RolesTab() {
  return (
    <ToastProvider>
      <RolesTabContent />
    </ToastProvider>
  );
}

function RolesTabContent() {
  const { show } = useToast();
  const [companyRoles, setCompanyRoles] = useState<RbacRole[]>([]);
  const [otRoles, setOtRoles] = useState<RbacRole[]>([]);
  const [companyStatus, setCompanyStatus] = useState<"loading" | "error" | "ready">("loading");
  const [otStatus, setOtStatus] = useState<"loading" | "error" | "ready">("loading");
  const [busyIds, setBusyIds] = useState<Record<string, boolean>>({});
  const [showCreateRole, setShowCreateRole] = useState(false);
  const [editTarget, setEditTarget] = useState<{ role: RbacRole; targetEntityType: RoleTargetEntityType } | null>(null);

  const loadRoles = useCallback((type: RoleTargetEntityType) => {
    const setStatus = type === "COMPANY" ? setCompanyStatus : setOtStatus;
    const setRoles = type === "COMPANY" ? setCompanyRoles : setOtRoles;
    setStatus("loading");
    superadminClient
      .listRoles(type)
      .then((data) => {
        setRoles(data);
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    loadRoles("COMPANY");
    loadRoles("TRANSIT_OFFICE");
  }, [loadRoles]);

  async function handleToggleActive(role: RbacRole, targetEntityType: RoleTargetEntityType) {
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

  async function handleDelete(role: RbacRole, targetEntityType: RoleTargetEntityType) {
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
        const columns: DataTableColumn<RbacRole>[] = [
          {
            key: "name",
            header: "Nombre",
            render: (r) => (
              <div className="min-w-0">
                <p className="font-semibold truncate">{r.name}</p>
                <p className="font-mono text-[10px] opacity-60">{r.code}</p>
              </div>
            ),
          },
          { key: "desc", header: "Descripción", render: (r) => <span className="opacity-70">{r.description ?? "—"}</span> },
          {
            key: "perms",
            header: "Permisos",
            align: "center",
            render: (r) => <span className="font-bold" style={{ color: "#557EFF" }}>{r.permissionCount}</span>,
          },
          {
            key: "estado",
            header: "Estado",
            align: "center",
            render: (r) => <StatusBadge tone={activeTone(r.isActive)} label={r.isActive ? "Activo" : "Inactivo"} />,
          },
          {
            key: "actions",
            header: "Acciones",
            align: "right",
            render: (r) => (
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
            ),
          },
        ];
        return (
          <section key={type} className="flex flex-col gap-2">
            <h3 className="flex items-center gap-2 text-sm font-bold">
              <Icon className="h-4 w-4" style={{ color: "#557EFF" }} aria-hidden="true" />
              {title}
            </h3>
            <DataTable
              columns={columns}
              rows={roles}
              getRowKey={(r) => r.id}
              status={status}
              emptyMessage={`No hay roles ${type === "COMPANY" ? "de compañía" : "de organismo de tránsito"}. Crea el primero.`}
              minWidth={640}
            />
          </section>
        );
      })}

      {showCreateRole && (
        <CreateRoleModal
          onClose={() => setShowCreateRole(false)}
          onCreated={(targetEntityType) => {
            setShowCreateRole(false);
            loadRoles(targetEntityType);
          }}
        />
      )}

      {editTarget && (
        <EditRolePermissionsModal
          role={editTarget.role}
          onClose={() => setEditTarget(null)}
          onSaved={() => {
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
            <div className="ml-6 grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-1">
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

/** HU #10664 — RBAC puro: los módulos son transversales (no dependen del tipo de entidad).
 * El catálogo se carga una sola vez y es el mismo para cualquier rol. */
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
  onCreated: (targetEntityType: RoleTargetEntityType) => void;
}) {
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [targetEntityType, setTargetEntityType] = useState<RoleTargetEntityType>("COMPANY");
  const { modules, loading: modulesLoading } = useModulesCatalog();
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
    // HU #10664 AC2 — un rol debe otorgar acceso a al menos un permiso (mínimo uno por módulo).
    if (selected.size === 0) {
      setError("Selecciona al menos un permiso para el rol (mínimo uno por módulo).");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const { id } = await superadminClient.createRole({
        targetEntityType,
        code: code.trim(),
        name: name.trim(),
        description: description.trim() || undefined,
      });
      await superadminClient.setRolePermissions(id, [...selected]);
      onCreated(targetEntityType);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "";
      setError(msg.includes("ROLE_CODE_DUPLICATE") ? "Este código ya existe para este tipo de entidad." : "Error al crear el rol.");
      setBusy(false);
    }
  }

  return (
    <Modal open onClose={onClose} busy={busy} size="md" title="Nuevo rol" titleClassName="text-lg font-bold text-[#162744] dark:text-white">
      <form onSubmit={handleSubmit} className="space-y-3">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
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
  role, onClose, onSaved,
}: {
  role: RbacRole;
  onClose: () => void;
  onSaved: (permissions: RbacRoleDetail["permissions"]) => void;
}) {
  // HU #10664 — RBAC puro: el checklist muestra todos los módulos (transversal).
  const { modules, loading: modulesLoading } = useModulesCatalog();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [loadingCurrent, setLoadingCurrent] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    superadminClient
      .getRole(role.id)
      .then((detail) => {
        if (cancelled) return;
        setSelected(new Set(detail.permissions.map((p) => p.id)));
      })
      .catch(() => {
        if (!cancelled) setError("No se pudieron recuperar los permisos actuales de este rol.");
      })
      .finally(() => {
        if (!cancelled) setLoadingCurrent(false);
      });
    return () => {
      cancelled = true;
    };
  }, [role.id]);

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
    // HU #10664 AC2 — un rol debe conservar acceso a al menos un permiso (mínimo uno por módulo).
    if (selected.size === 0) {
      setError("Selecciona al menos un permiso para el rol (mínimo uno por módulo).");
      return;
    }
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
        {loadingCurrent ? (
          <div className="py-10 text-center text-sm opacity-60">Cargando permisos actuales…</div>
        ) : (
          <ModulePermissionsChecklist
            modules={modules}
            modulesLoading={modulesLoading}
            selected={selected}
            onToggle={toggle}
            onToggleModule={toggleModule}
          />
        )}
        {error && <p role="alert" className="text-xs py-2 px-3 rounded-xl font-medium" style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}>{error}</p>}
        <button
          type="submit"
          disabled={busy || loadingCurrent}
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
