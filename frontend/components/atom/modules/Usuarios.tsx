"use client";

import { useState } from "react";
import { Plus, Search, X, Building2, Users, Shield } from "lucide-react";
import { createInvitation } from "@/lib/api/security";
import { ApiError } from "@/lib/api/types";
import { ModuleTitle } from "./ModuleTitle";

const USERS = [
  { n: "Mateo Ruiz Gil", e: "mateo@flit.io", r: "Súper Admin", a: true, la: "21/05/2026 06:00 p.m." },
  { n: "Juan Pérez", e: "juan@tenant.com", r: "Gestor", a: true, la: "21/05/2026 03:12 p.m." },
  { n: "Sonia Cadavid Ríos", e: "sonicr@tenant.com", r: "Súper Admin", a: false, la: "21/05/2026 06:00 p.m." },
  { n: "Laura Bedoya Ríos", e: "labedo@tenant.com", r: "Auditor", a: true, la: "21/05/2026 06:00 p.m." },
];

const COMPANIES = [
  { name: "FLIT SAS", nit: "900.123.456-7", estado: "Activa", plan: "Enterprise", users: 250 },
  { name: "Movilidad Antioquia", nit: "890.456.789-1", estado: "Activa", plan: "Profesional", users: 84 },
  { name: "Transito Sabaneta", nit: "890.111.222-3", estado: "Suspendida", plan: "Básico", users: 12 },
  { name: "Operador Valle", nit: "890.333.444-5", estado: "En prueba", plan: "Trial", users: 6 },
];

const PERMISSIONS = ["Trámites", "Reportes", "Validaciones", "Usuarios", "Admin OT", "Documentos"];
const ROLES = ["Super Admin", "Admin Tenant", "Gestor", "Auditor"];

const TABS = [
  { id: "equipo", label: "Equipo", icon: Users },
  { id: "roles", label: "Roles y permisos", icon: Shield },
  { id: "companias", label: "Compañías", icon: Building2 },
] as const;

type TabId = (typeof TABS)[number]["id"];

const BADGE: Record<string, string> = {
  Activa: "#00DBD5",
  Suspendida: "#FF4E00",
  "En prueba": "#F9AC00",
};

export function Usuarios() {
  const [open, setOpen] = useState(false);
  const [tab, setTab] = useState<TabId>("equipo");
  const [list, setList] = useState(USERS);
  const [pendingInvites, setPendingInvites] = useState<{ email: string }[]>([]);

  function handleInviteSuccess(email: string) {
    setPendingInvites((prev) => [...prev, { email }]);
  }

  return (
    <div className="h-full w-full px-6 pt-5 pb-24 flex flex-col gap-4 overflow-hidden">
      <ModuleTitle
        title="Administración de usuarios y permisos"
        subtitle="Gestiona el acceso de tu equipo a la plataforma."
        right={
          tab === "equipo" ? (
            <button onClick={() => setOpen(true)} className="flex items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white" style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}>
              <Plus className="h-4 w-4" /> Invitar Colaborador
            </button>
          ) : tab === "companias" ? (
            <button className="flex items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white" style={{ background: "#557EFF" }}>
              <Plus className="h-4 w-4" /> Nueva compañía
            </button>
          ) : (
            <button className="flex items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white" style={{ background: "#557EFF" }}>
              <Plus className="h-4 w-4" /> Nuevo rol
            </button>
          )
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
              {active && <span className="absolute left-2 right-2 -bottom-px h-0.5 rounded-full" style={{ background: "#557EFF" }} />}
            </button>
          );
        })}
      </div>

      {tab === "equipo" && (
        <>
          <div className="grid grid-cols-4 gap-3 shrink-0">
            {[
              ["Total Usuarios", 250, "#557EFF"],
              ["Usuarios Activos", 240, "#00DBD5"],
              ["Usuarios inactivos", 10, "#FF4E00"],
              ["Súper admin", 5, "#162744"],
            ].map(([l, v, c]) => (
              <div key={l as string} className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
                <p className="text-[11px] opacity-70 font-medium">{l as string}</p>
                <p className="text-3xl font-bold mt-1" style={{ color: c as string }}>{v as number}</p>
              </div>
            ))}
          </div>
          <div className="flex items-center gap-2 p-2.5 rounded-xl border bg-white dark:bg-[#0B0F14] max-w-md shrink-0" style={{ borderColor: "#DFE5ED" }}>
            <Search className="h-4 w-4 opacity-60" />
            <input placeholder="Buscar por nombre, correo..." className="flex-1 bg-transparent outline-none text-xs" />
          </div>
          <div className="flex-1 min-h-0 flex flex-col">
            <div className="grid grid-cols-12 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl" style={{ background: "#DFE5ED", color: "#162744" }}>
              <div className="col-span-4">Usuario</div>
              <div className="col-span-2">Rol</div>
              <div className="col-span-2">Estado</div>
              <div className="col-span-3">Último acceso</div>
              <div className="col-span-1 text-right">Activo</div>
            </div>
            <div className="flex-1 overflow-y-auto space-y-2 pt-2">
              {list.map((u, i) => (
                <div key={u.e} className="grid grid-cols-12 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs" style={{ borderColor: "#DFE5ED" }}>
                  <div className="col-span-4">
                    <p className="font-semibold">{u.n}</p>
                    <p className="text-[10px] opacity-60">{u.e}</p>
                  </div>
                  <div className="col-span-2 opacity-80">{u.r}</div>
                  <div className="col-span-2"><span className="px-2 py-0.5 rounded-full text-[10px] font-semibold text-white" style={{ background: u.a ? "#00DBD5" : "#FF4E00" }}>{u.a ? "Activo" : "Inactivo"}</span></div>
                  <div className="col-span-3 opacity-70">{u.la}</div>
                  <div className="col-span-1 flex justify-end">
                    <button onClick={() => setList((L) => L.map((x, j) => j === i ? { ...x, a: !x.a } : x))} className="w-10 h-5 rounded-full relative transition" style={{ background: u.a ? "#00DBD5" : "#DFE5ED" }}>
                      <span className="absolute top-0.5 h-4 w-4 rounded-full bg-white transition-all" style={{ left: u.a ? "calc(100% - 18px)" : "2px" }} />
                    </button>
                  </div>
                </div>
              ))}
              {pendingInvites.map((inv) => (
                <div key={inv.email} className="grid grid-cols-12 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs" style={{ borderColor: "#DFE5ED", opacity: 0.8 }}>
                  <div className="col-span-4">
                    <p className="font-semibold italic opacity-60">Invitación pendiente</p>
                    <p className="text-[10px] opacity-60">{inv.email}</p>
                  </div>
                  <div className="col-span-2 opacity-40">—</div>
                  <div className="col-span-2"><span className="px-2 py-0.5 rounded-full text-[10px] font-semibold text-white" style={{ background: "#F9AC00" }}>Pendiente</span></div>
                  <div className="col-span-3 opacity-40">—</div>
                  <div className="col-span-1 flex justify-end">
                    <div className="w-10 h-5 rounded-full" style={{ background: "#DFE5ED" }} />
                  </div>
                </div>
              ))}
            </div>
            <p className="text-[10px] opacity-60 text-right pt-2 shrink-0">Mostrando {list.length} de 250  «  1 2 ... 10  »</p>
          </div>
        </>
      )}

      {tab === "roles" && (
        <div className="flex-1 min-h-0 flex flex-col gap-3">
          <div className="grid grid-cols-4 gap-3 shrink-0">
            {[
              ["Super Admin", "12/12", "#00DBD5"],
              ["Admin Tenant", "9/12", "#557EFF"],
              ["Gestor", "6/12", "#F9AC00"],
              ["Auditor", "4/12", "#162744"],
            ].map(([role, scope, color]) => (
              <div key={role as string} className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
                <p className="text-xs font-semibold">{role as string}</p>
                <p className="text-[10px] font-semibold mt-1" style={{ color: color as string }}>{scope as string} permisos</p>
                <div className="h-2 rounded-full mt-2 overflow-hidden" style={{ background: "#DFE5ED" }}>
                  <div className="h-full" style={{ width: scope === "12/12" ? "100%" : scope === "9/12" ? "75%" : scope === "6/12" ? "50%" : "34%", background: color as string }} />
                </div>
              </div>
            ))}
          </div>
          <div className="flex-1 min-h-0 rounded-2xl overflow-hidden flex flex-col bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
            <div className="grid px-4 py-2.5 text-[10px] font-semibold uppercase shrink-0" style={{ gridTemplateColumns: `180px repeat(${ROLES.length}, 1fr)`, background: "#DFE5ED", color: "#162744" }}>
              <div>Módulo</div>
              {ROLES.map((r) => <div key={r} className="text-center">{r}</div>)}
            </div>
            <div className="flex-1 overflow-y-auto">
              {PERMISSIONS.map((perm) => (
                <div key={perm} className="grid items-center px-4 py-3 border-b text-xs" style={{ gridTemplateColumns: `180px repeat(${ROLES.length}, 1fr)`, borderColor: "#DFE5ED" }}>
                  <div className="font-medium">{perm}</div>
                  {ROLES.map((role, i) => (
                    <div key={role} className="flex justify-center">
                      <input type="checkbox" defaultChecked={i === 0 || (i === 1 && perm !== "Admin OT")} className="h-4 w-4 accent-[#557EFF]" />
                    </div>
                  ))}
                </div>
              ))}
            </div>
            <div className="px-4 py-3 border-t flex justify-end gap-2 shrink-0" style={{ borderColor: "#DFE5ED" }}>
              <button className="px-4 py-2 rounded-xl text-xs font-medium border" style={{ borderColor: "#DFE5ED" }}>Descartar</button>
              <button className="px-4 py-2 rounded-xl text-xs font-semibold text-white" style={{ background: "#557EFF" }}>Guardar permisos</button>
            </div>
          </div>
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

      {open && <InviteModal onClose={() => setOpen(false)} onSuccess={handleInviteSuccess} />}
    </div>
  );
}

function InviteModal({ onClose, onSuccess }: { onClose: () => void; onSuccess: (email: string) => void }) {
  const [email, setEmail] = useState("");
  const [roleId, setRoleId] = useState("");
  const [selectedRole, setSelectedRole] = useState(0);
  const [status, setStatus] = useState<"idle" | "loading" | "done" | "done_no_email">("idle");
  const [error, setError] = useState<string | null>(null);
  const [invitedEmail, setInvitedEmail] = useState("");

  const isDone = status === "done" || status === "done_no_email";

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setStatus("loading");
    try {
      const result = await createInvitation(email.trim(), roleId.trim());
      setInvitedEmail(result.email);
      setStatus(result.emailSent ? "done" : "done_no_email");
      onSuccess(result.email);
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
            <h3 className="text-lg font-bold">Invitar colaborador</h3>
            <p className="text-xs opacity-70 mt-0.5">Asigna el acceso y permiso para colaborar dentro de FLIT.</p>
          </div>
          <button onClick={onClose}><X className="h-5 w-5" /></button>
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
            <input
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="correo@empresa.com"
              className="w-full text-sm px-3 py-2.5 rounded-xl border bg-transparent outline-none focus:border-[#557EFF]"
              style={{ borderColor: "#DFE5ED" }}
            />
            <div>
              <p className="text-xs font-semibold mb-2">Rol del usuario</p>
              <div className="grid grid-cols-2 gap-2">
                {["Administrador", "Gestor", "Operador", "Visualizador"].map((r, i) => (
                  <label
                    key={r}
                    className="flex items-center gap-2 text-xs p-2 rounded-lg border cursor-pointer transition"
                    style={{ borderColor: selectedRole === i ? "#557EFF" : "#DFE5ED" }}
                  >
                    <input type="radio" name="rol" checked={selectedRole === i} onChange={() => setSelectedRole(i)} /> {r}
                  </label>
                ))}
              </div>
            </div>
            <div>
              <p className="text-[10px] opacity-60 mb-1">ID de rol (UUID del tenant)</p>
              <input
                type="text"
                required
                value={roleId}
                onChange={(e) => setRoleId(e.target.value)}
                placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                className="w-full text-xs px-3 py-2.5 rounded-xl border bg-transparent outline-none font-mono focus:border-[#557EFF]"
                style={{ borderColor: "#DFE5ED" }}
              />
            </div>
            {error && (
              <p className="text-xs py-2 px-3 rounded-xl font-medium" style={{ background: "rgba(255,78,0,0.08)", color: "#FF4E00" }}>{error}</p>
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
