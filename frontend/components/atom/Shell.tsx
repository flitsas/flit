"use client";

import { ReactNode, useEffect, useRef, useState } from "react";
import { usePathname } from "next/navigation";
import { canReadIctLogs, canReadLogQx, decodeJwtPayload, isAdminCompany, isOtAdmin, isSuperAdmin, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { fetchOtProfile } from "@/lib/api/admin-ot";
import {
  isOtHubSegmentActive,
  OT_ADM_DOCK,
  otHubListPath,
  resolveOtHubHref,
  type OtHubTabId,
} from "@/components/admin/transit-offices/ot-nav";
import { OT_ADMIN_SPA_OMIT } from "@/lib/nav/modules";
import { useDockScrollCondense } from "./useDockScrollCondense";
import { buildDockGroups, flattenDockEntries } from "./dock/dockGroups";
import { DockDesktop } from "./dock/DockDesktop";
import { DrFlitAssistant } from "@/components/dr-flit";

const logoWhite = "/assets/logo-flit-white.svg";
const logoDark = "/assets/logo-flit-dark.svg";
const fabIcon = "/assets/favicon.svg";
import {
  LayoutGrid,
  FileStack,
  BarChart3,
  FileSpreadsheet,
  ShieldCheck,
  Users,
  HelpCircle,
  Building2,
  Bell,
  Sun,
  Moon,
  MoreVertical,
  KeyRound,
  LogOut,
  FolderCog,
  Lock,
  Landmark,
  Fingerprint,
  Send,
  ScrollText,
  Radar,
  Network,
  Route,
  X,
  Scale,
  FileText,
  ClipboardList,
  Tag,
  ListChecks,
  Monitor,
  FileSignature,
} from "lucide-react";

export type ModuleId =
  | "dashboard"
  | "tramites"
  | "reportes"
  | "reportes-detallados"
  | "validaciones"
  | "usuarios"
  | "ayuda"
  | "rbac"
  | "auditoria"
  | "log-qx"
  | "ict-logs"
  | "ict-reportes"
  | "ict-trazabilidad";

const DOCK: { id: ModuleId; label: string; icon: typeof LayoutGrid }[] = [
  // Dashboard no va en el dock: el FAB central (Inicio FLIT) abre el mismo módulo.
  { id: "tramites", label: "Trámites", icon: FileStack },
  { id: "reportes", label: "Reportes", icon: BarChart3 },
  { id: "reportes-detallados", label: "Reportes Detallados", icon: FileSpreadsheet },
  { id: "validaciones", label: "Identidad", icon: ShieldCheck },
  { id: "usuarios", label: "Usuarios", icon: Users },
  { id: "ayuda", label: "Ayuda", icon: HelpCircle },
];

// Entrada normalizada del dock: módulos de la SPA y accesos admin/empresa comparten
// la misma forma para poder repartirse balanceadamente alrededor del FAB.
type DockEntry = {
  key: string;
  label: string;
  icon: typeof LayoutGrid;
  active: boolean;
  onClick: () => void;
  /** Submenú anidado (Administradores → Plataforma → Mandatos). */
  children?: DockEntry[];
};

function useTheme() {
  const [dark, setDark] = useState<boolean>(() => {
    if (typeof window === "undefined") return false;
    return localStorage.getItem("flit-theme") === "dark";
  });
  useEffect(() => {
    document.documentElement.classList.toggle("dark", dark);
    localStorage.setItem("flit-theme", dark ? "dark" : "light");
  }, [dark]);
  return { dark, toggle: () => setDark((d) => !d) };
}

function useCurrentUser() {
  const [user] = useState(() => {
    if (typeof window === "undefined") return null;
    const token = window.localStorage.getItem(TOKEN_STORAGE_KEY);
    const payload = decodeJwtPayload(token);
    if (!payload) return null;
    // HU #10506 (multi-rol): con 2+ roles, el backend serializa role_code/role como
    // ARRAY (colapso de claims .NET), no como string — por eso la fuente confiable es
    // siempre el claim `roles` (array explícito de {id, code}), con role_code/role como
    // fallback solo cuando de verdad vienen como string (usuario con un único rol).
    const roleCodes = Array.isArray(payload.roles)
      ? payload.roles
          .map((r) => r?.code)
          .filter((c): c is string => typeof c === "string")
      : [];
    if (roleCodes.length === 0) {
      if (typeof payload.role_code === "string") roleCodes.push(payload.role_code);
      else if (typeof payload.role === "string") roleCodes.push(payload.role);
    }
    const roleLabel = roleCodes.includes("SuperAdmin")
      ? "Super Admin"
      : roleCodes.includes("AdminCompany")
        ? "Admin de Compañía"
        : roleCodes.includes("ot_admin")
          ? "Admin OT"
          : roleCodes[0] || "Usuario";
    return {
      displayName:
        (payload.display_name as string | undefined) ??
        (payload.email as string | undefined) ??
        "Usuario",
      email: (payload.email as string | undefined) ?? "",
      roleLabel,
      tenantName: payload.tenant_name ?? null,
      isSuperAdmin: isSuperAdmin(payload),
      isAdminCompany: isAdminCompany(payload),
      isOtAdmin: isOtAdmin(payload),
      canReadLogQx: canReadLogQx(payload),
      canReadIctLogs: canReadIctLogs(payload),
    };
  });
  return user;
}

export function Shell({
  children,
  active,
  onNav,
  onLogout,
  visibleModuleCodes,
}: {
  children: ReactNode;
  active: ModuleId;
  onNav: (v: ModuleId) => void;
  onLogout?: () => void;
  /** When provided, only dock items whose id is in this list are shown. */
  visibleModuleCodes?: string[];
}) {
  const { dark, toggle } = useTheme();
  const logoSrc = dark ? logoWhite : logoDark;
  const currentUser = useCurrentUser();

  const [menuOpen, setMenuOpen] = useState(false);
  // HU #10844 — En <lg el dock horizontal (hasta ~14 entradas para SuperAdmin) no cabe;
  // se colapsa a un lanzador que abre una grilla de apps controlada por este estado.
  const [dockOpen, setDockOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const contentScrollRef = useRef<HTMLDivElement>(null);
  const dockLauncherRef = useRef<HTMLButtonElement>(null);
  const condensed = useDockScrollCondense(contentScrollRef);

  useEffect(() => {
    const h = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    document.addEventListener("mousedown", h);
    return () => document.removeEventListener("mousedown", h);
  }, []);

  // Menú móvil: Escape cierra y devuelve el foco al lanzador (GUIA-DOCK §9).
  useEffect(() => {
    if (!dockOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setDockOpen(false);
      dockLauncherRef.current?.focus();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [dockOpen]);

  // Filtra los módulos del dock según permisos RBAC del JWT cuando visibleModuleCodes
  // está disponible. "Ayuda" es soporte universal (no es un módulo con permiso RBAC),
  // por lo que se muestra siempre, en todas las pantallas del dock.
  // Admin OT: las pestañas del hub viven en el dock (Trámites / Usuarios / Reportes / …);
  // se omiten los módulos SPA homónimos para no duplicar píldoras (OT_ADMIN_SPA_OMIT
  // compartido con resolveNavigableModuleIds — invariante dock ≡ URL).
  const visibleDock = (visibleModuleCodes
    ? DOCK.filter((it) => it.id === "ayuda" || visibleModuleCodes.includes(it.id))
    : DOCK
  ).filter((it) => !(currentUser?.isOtAdmin && OT_ADMIN_SPA_OMIT.has(it.id)));

  // Una sola lista con TODAS las entradas del dock (módulos + botones admin/empresa
  // según rol). El FAB de inicio va siempre en el centro y las entradas se reparten
  // de forma balanceada a izquierda/derecha; si se agregan más, se redistribuyen solas.
  const pathname = usePathname() ?? "";
  const onAdminRoute = pathname.startsWith("/admin") || pathname.startsWith("/empresa");

  const goOtHub = (tab: OtHubTabId) => {
    void resolveOtHubHref(tab, pathname, "ot_admin", async () => {
      const profile = await fetchOtProfile();
      return profile.transitOfficeId;
    }).then((href) => {
      window.location.assign(href);
    });
  };

  const entries: DockEntry[] = visibleDock.map((it) => ({
    key: it.id,
    label: it.label,
    icon: it.icon,
    active: !onAdminRoute && active === it.id,
    onClick: () => onNav(it.id),
  }));

  if (currentUser?.isSuperAdmin) {
    entries.push(
      {
        key: "admin-companies",
        label: "Compañías",
        icon: Building2,
        active: pathname.startsWith("/admin/companies"),
        onClick: () => window.location.assign("/admin/companies"),
      },
      {
        // Tránsito pasa a ser contenedor (mismo patrón que Plataforma): el catálogo de causales
        // alimenta el modal de rechazo del organismo, así que cuelga de aquí y no de Compañías.
        key: "admin-transit",
        label: "Tránsito",
        icon: Landmark,
        active:
          pathname.startsWith("/admin/transit-offices") ||
          pathname.startsWith("/admin/causales-rechazo"),
        onClick: () => undefined,
        children: [
          {
            key: "admin-transit-offices",
            label: "Organismos",
            icon: Landmark,
            active: pathname.startsWith("/admin/transit-offices"),
            onClick: () => window.location.assign(otHubListPath()),
          },
          {
            key: "admin-rejection-reasons",
            label: "Causales de rechazo",
            icon: ClipboardList,
            active: pathname.startsWith("/admin/causales-rechazo"),
            onClick: () => window.location.assign("/admin/causales-rechazo"),
          },
        ],
      },
      {
        key: "admin-documents",
        label: "Documental",
        icon: FolderCog,
        active: pathname.startsWith("/admin/documents"),
        onClick: () => window.location.assign("/admin/documents"),
      },
      {
        key: "admin-improntas",
        label: "Improntas",
        icon: Fingerprint,
        active: pathname.startsWith("/admin/improntas"),
        onClick: () => window.location.assign("/admin/improntas"),
      },
      {
        key: "admin-quipux",
        label: "Quipux",
        icon: Send,
        active: pathname.startsWith("/admin/quipux"),
        onClick: () => window.location.assign("/admin/quipux"),
      },
      {
        key: "rbac",
        label: "RBAC Admin",
        icon: Lock,
        active: !onAdminRoute && active === "rbac",
        onClick: () => onNav("rbac"),
      },
      {
        key: "auditoria",
        label: "Auditoría",
        icon: ScrollText,
        active: !onAdminRoute && active === "auditoria",
        onClick: () => onNav("auditoria"),
      },
      {
        key: "admin-plataforma",
        label: "Plataforma",
        icon: Monitor,
        active: pathname.startsWith("/admin/plataforma"),
        onClick: () => undefined,
        children: [
          {
            key: "admin-tipos-tramite",
            label: "Tipos de trámites",
            icon: ListChecks,
            active: pathname.startsWith("/admin/plataforma/tipos-tramite"),
            onClick: () => window.location.assign("/admin/plataforma/tipos-tramite"),
          },
          {
            key: "admin-mandatos",
            label: "Mandatos",
            icon: FileSignature,
            active: pathname.startsWith("/admin/plataforma/mandatos"),
            onClick: () => window.location.assign("/admin/plataforma/mandatos"),
          },
          {
            key: "admin-fur",
            label: "FUR",
            icon: FileText,
            active: pathname.startsWith("/admin/plataforma/fur"),
            onClick: () => window.location.assign("/admin/plataforma/fur"),
          },
          {
            key: "admin-notificaciones",
            label: "Notificaciones",
            icon: Bell,
            active: pathname.startsWith("/admin/plataforma/notificaciones"),
            onClick: () => window.location.assign("/admin/plataforma/notificaciones"),
          },
        ],
      },
    );
  }

  // Admin OT: pestañas del hub trasladadas al dock (Administración = Reglas/Docs/Requisitos;
  // Trámites, Preasignación, Usuarios y Reportes como ítems del dock). Sin Compañías/RBAC.
  if (currentUser?.isOtAdmin) {
    entries.push(
      {
        key: OT_ADM_DOCK.tramites,
        label: "Trámites",
        icon: FileStack,
        active: isOtHubSegmentActive(pathname, "client-procedures"),
        onClick: () => goOtHub("client-procedures"),
      },
      {
        key: OT_ADM_DOCK.rules,
        label: "Reglas",
        icon: Scale,
        active: isOtHubSegmentActive(pathname, "rules"),
        onClick: () => goOtHub("rules"),
      },
      {
        key: OT_ADM_DOCK.documents,
        label: "Documentos",
        icon: FileText,
        active: isOtHubSegmentActive(pathname, "documents"),
        onClick: () => goOtHub("documents"),
      },
      {
        key: OT_ADM_DOCK.requirements,
        label: "Requisitos",
        icon: ClipboardList,
        active: isOtHubSegmentActive(pathname, "requirements"),
        onClick: () => goOtHub("requirements"),
      },
      {
        key: OT_ADM_DOCK.preasignacion,
        label: "Preasignación",
        icon: Tag,
        active: isOtHubSegmentActive(pathname, "plate-ranges"),
        onClick: () => goOtHub("plate-ranges"),
      },
      {
        key: OT_ADM_DOCK.usuarios,
        label: "Usuarios",
        icon: Users,
        active: isOtHubSegmentActive(pathname, "usuarios"),
        onClick: () => goOtHub("usuarios"),
      },
      {
        key: OT_ADM_DOCK.reportes,
        label: "Reportes",
        icon: BarChart3,
        active: isOtHubSegmentActive(pathname, "reportes"),
        onClick: () => goOtHub("reportes"),
      },
      {
        key: OT_ADM_DOCK.mandatos,
        label: "Mandatos",
        icon: FileSignature,
        active: isOtHubSegmentActive(pathname, "mandatos"),
        onClick: () => goOtHub("mandatos"),
      },
    );
  }

  if (currentUser?.isAdminCompany) {
    // Gestor: una sola entrada "Administración" → consola de su compañía (RL, baúl, escrituras…).
    // No se empuja "Usuarios" en este menú (req. menú admin gestor); el módulo Usuarios sigue
    // disponible vía dock RBAC `usuarios` si el rol lo tiene concedido.
    entries.push({
      key: "admin-companies",
      label: "Administración",
      icon: Building2,
      // AdminCompany: /admin/companies redirige al configurador de su tenant (HU #11228).
      active: pathname.startsWith("/admin/companies"),
      onClick: () => window.location.assign("/admin/companies"),
    });
  }

  // LOG QX (HU #10795): trazabilidad Quipux. Agrupador "Integraciones" (ex Soporte),
  // gateado por `logqx.read` (o SuperAdmin vía canReadLogQx). No se duplica en el bloque
  // isSuperAdmin de arriba.
  if (currentUser?.canReadLogQx) {
    entries.push({
      key: "log-qx",
      label: "Log QX",
      icon: Radar,
      active: !onAdminRoute && active === "log-qx",
      onClick: () => onNav("log-qx"),
    });
  }

  // ICT (Integración con Terceros, HU10893) — gate por el permiso `ict.logs.read` (o SuperAdmin).
  // Vive en el agrupador "Integraciones" junto a LOG QX.
  //
  // Es contenedor, no destino (mismo patrón que Tránsito y Plataforma): cuelga "Log ICT" —los logs
  // técnicos y sus alertas— y "Reportes ICT" —informes en vivo, consultas y programación (HU
  // #11619)—. Separarlos en dos píldoras hermanas dejaba dos entradas sueltas sin decir que hablan
  // del mismo sistema; anidarlas bajo "ICT" nombra primero el sistema y luego qué se quiere de él.
  // Ambas hojas comparten gate: es el mismo público.
  if (currentUser?.canReadIctLogs) {
    entries.push({
      key: "ict",
      label: "ICT",
      icon: Network,
      active: !onAdminRoute
        && (active === "ict-logs" || active === "ict-reportes" || active === "ict-trazabilidad"),
      onClick: () => undefined,
      children: [
        {
          key: "ict-logs",
          label: "Log ICT",
          icon: Network,
          active: !onAdminRoute && active === "ict-logs",
          onClick: () => onNav("ict-logs"),
        },
        {
          key: "ict-trazabilidad",
          label: "Trazabilidad ICT",
          icon: Route,
          active: !onAdminRoute && active === "ict-trazabilidad",
          onClick: () => onNav("ict-trazabilidad"),
        },
        {
          key: "ict-reportes",
          label: "Reportes ICT",
          icon: BarChart3,
          active: !onAdminRoute && active === "ict-reportes",
          onClick: () => onNav("ict-reportes"),
        },
      ],
    });
  }

  // Agrupadores del dock (menú / submenú). Solo se muestran grupos con al menos
  // un ítem visible según permisos/rol. FAB de inicio queda centrado entre grupos.
  const groups = buildDockGroups(entries);
  const atBottom = condensed;

  return (
    <div
      className="h-screen w-full overflow-hidden flex flex-col"
      style={{
        background: dark ? "#05060A" : "#EEF5FF",
        color: dark ? "#FFFFFF" : "#162744",
        fontFamily: "Poppins, sans-serif",
      }}
    >
      {/* Header */}
      <header
        className="shrink-0 flex items-center justify-between px-4 md:px-6 py-3 border-b"
        style={{ borderColor: dark ? "rgba(255,255,255,0.08)" : "#DFE5ED" }}
      >
        <div className="flex items-center gap-3">
          <img src={logoSrc} alt="FLIT 2.0" className="h-10 w-auto" />
        </div>
        <div className="flex items-center gap-3">
          {/* Theme toggle */}
          <button
            onClick={toggle}
            aria-label="Cambiar tema"
            className="flex items-center gap-1 rounded-full px-1 py-1 transition"
            style={{ background: "#00DBD5", color: "#162744" }}
          >
            <span className={`h-7 w-7 grid place-items-center rounded-full ${dark ? "" : "bg-white"}`}>
              <Sun className="h-3.5 w-3.5" />
            </span>
            <span className={`h-7 w-7 grid place-items-center rounded-full ${dark ? "bg-white" : ""}`}>
              <Moon className="h-3.5 w-3.5" />
            </span>
          </button>
          <div className="hidden sm:flex flex-col items-end leading-tight">
            <span className="text-[10px] font-medium" style={{ color: "#557EFF" }}>
              {currentUser?.roleLabel ?? "—"}
            </span>
            {currentUser?.tenantName && (
              <span className="text-[10px] opacity-55">
                {currentUser.tenantName}
              </span>
            )}
            <span className="text-xs font-semibold">
              {currentUser?.displayName ?? currentUser?.email ?? "—"}
            </span>
          </div>
          <div
            className="h-9 w-9 rounded-full grid place-items-center border-2 text-xs font-bold text-white select-none"
            style={{
              borderColor: "#00DBD5",
              background: "linear-gradient(135deg,#557EFF,#00DBD5)",
            }}
            aria-label="Avatar"
          >
            {(currentUser?.displayName?.[0] ?? currentUser?.email?.[0] ?? "U").toUpperCase()}
          </div>
          <div className="relative" ref={menuRef}>
            <button
              onClick={() => setMenuOpen((v) => !v)}
              className="p-1 rounded-md hover:bg-black/5 dark:hover:bg-white/10"
              aria-label="Menú de usuario"
            >
              <MoreVertical className="h-5 w-5" />
            </button>
            {menuOpen && (
              <div
                className="absolute right-0 top-full mt-2 w-60 rounded-xl py-1.5 z-50 text-xs"
                style={{
                  background: dark ? "#0B0F14" : "#FFFFFF",
                  border: `1px solid ${dark ? "rgba(255,255,255,0.1)" : "#DFE5ED"}`,
                  boxShadow: "0 18px 40px -10px rgba(22,39,68,0.25)",
                  color: dark ? "#FFFFFF" : "#162744",
                }}
              >
                <MenuItem
                  icon={KeyRound}
                  label="Cambio de contraseña"
                  onClick={() => {
                    setMenuOpen(false);
                    window.location.href = "/profile/change-password";
                  }}
                />
                <div className="h-px my-1" style={{ background: dark ? "rgba(255,255,255,0.08)" : "#DFE5ED" }} />
                <MenuItem
                  icon={LogOut}
                  label="Salir de la plataforma"
                  danger
                  onClick={() => {
                    setMenuOpen(false);
                    onLogout?.();
                  }}
                />
              </div>
            )}
          </div>
        </div>
      </header>

      {/* Main */}
      <main className="flex-1 min-h-0 overflow-hidden relative">
        {/* AC1 #10498: el scroll ocurre DENTRO del área de contenido (no se clipa) y el
            padding inferior libera el dock flotante para que nada quede oculto tras él.
            `data-shell-scroll` lo usa el wizard (tracker sticky al tope de este contenedor). */}
        <div
          ref={contentScrollRef}
          className="absolute inset-0 overflow-y-auto pb-28"
          data-shell-scroll
        >
          {children}
        </div>

        <DockDesktop
          groups={groups}
          atBottom={atBottom}
          onHome={() => onNav("dashboard")}
          homeActive={!onAdminRoute && active === "dashboard"}
        />

        {/* DR. FLIT — asistente conversacional (UI-only; sin APIs). */}
        <DrFlitAssistant
          displayName={currentUser?.displayName ?? currentUser?.email ?? null}
        />

        {/* Bottom dock — móvil/tablet (<lg): lanzador + hoja agrupada. */}
        <div className="lg:hidden">
          <button
            ref={dockLauncherRef}
            onClick={() => setDockOpen(true)}
            className="pointer-events-auto absolute left-1/2 -translate-x-1/2 bottom-5 z-40 h-14 w-14 overflow-hidden rounded-full transition-transform duration-[var(--nav-duracion)] ease-[var(--nav-ease)] hover:scale-105 motion-reduce:transition-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)] focus-visible:ring-offset-2"
            style={{ boxShadow: "var(--nav-sombra-activo)" }}
            aria-label="Abrir menú de navegación"
            aria-expanded={dockOpen}
            aria-controls="dock-mobile-sheet"
          >
            <img src={fabIcon} alt="" aria-hidden="true" className="h-full w-full object-cover" />
          </button>

          {dockOpen && (
            <div
              className="absolute inset-0 z-50 flex items-end justify-center p-4"
              style={{ background: "rgba(22, 39, 68, 0.45)", backdropFilter: "blur(4px)" }}
              onPointerDown={(e) => {
                if (e.target === e.currentTarget) {
                  setDockOpen(false);
                  dockLauncherRef.current?.focus();
                }
              }}
              role="dialog"
              aria-modal="true"
              aria-label="Navegación"
            >
              <div
                id="dock-mobile-sheet"
                className="dock-sheet w-full max-w-sm max-h-[min(70vh,32rem)] overflow-y-auto rounded-[var(--nav-radio-panel)] p-4"
                style={{
                  background: "var(--nav-panel-bg)",
                  border: "1px solid var(--nav-borde)",
                  boxShadow: "var(--nav-sombra-panel)",
                }}
              >
                <div className="mb-3 flex items-center justify-between">
                  <span className="text-xs font-semibold tracking-wide text-[var(--nav-texto-tenue)] uppercase">
                    Navegación
                  </span>
                  <button
                    onClick={() => {
                      setDockOpen(false);
                      dockLauncherRef.current?.focus();
                    }}
                    className="p-1 rounded-md hover:bg-[var(--nav-app-bg)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)]"
                    aria-label="Cerrar menú"
                  >
                    <X className="h-4 w-4" aria-hidden="true" />
                  </button>
                </div>
                <div className="flex flex-col gap-3">
                  {groups.map((g) => (
                    <div key={g.id}>
                      <p className="px-1 pb-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--nav-texto-tenue)]">
                        {g.label}
                      </p>
                      <div className="grid grid-cols-4 gap-2 sm:grid-cols-5">
                        {flattenDockEntries(g.items).map((it) => {
                          const Icon = it.icon;
                          return (
                            <button
                              key={it.key}
                              onClick={() => {
                                setDockOpen(false);
                                it.onClick();
                              }}
                              className={`dock-pill flex flex-col items-center gap-1 rounded-xl p-2 text-center transition-colors duration-[var(--nav-duracion)] ease-[var(--nav-ease)] motion-reduce:transition-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)] ${
                                it.active ? "font-semibold text-white" : "font-medium"
                              }`}
                              style={
                                it.active
                                  ? {
                                      background: "var(--nav-activo)",
                                      boxShadow: "var(--nav-sombra-activo)",
                                      color: "#ffffff",
                                    }
                                  : undefined
                              }
                              aria-current={it.active ? "page" : undefined}
                            >
                              <Icon className="h-5 w-5" strokeWidth={it.active ? 2.4 : 1.8} aria-hidden="true" />
                              <span className="text-[10px] leading-tight line-clamp-2">{it.label}</span>
                            </button>
                          );
                        })}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}
        </div>
      </main>

      {/* Footer */}
      <footer
        className="shrink-0 px-4 md:px-6 py-2 border-t text-[10px] text-center"
        style={{
          borderColor: dark ? "rgba(255,255,255,0.08)" : "#DFE5ED",
          color: dark ? "rgba(255,255,255,0.55)" : "rgba(22,39,68,0.6)",
        }}
      >
        Políticas de Privacidad y Términos de Uso · © 2026 FLIT · Todos los derechos reservados · Protegido por cifrado TLS · Auditoría continua · ISO 27001
      </footer>
    </div>
  );
}

function MenuItem({
  icon: Icon,
  label,
  onClick,
  danger,
}: {
  icon: typeof LayoutGrid;
  label: string;
  onClick: () => void;
  danger?: boolean;
}) {
  return (
    <button
      onClick={onClick}
      className="w-full flex items-center gap-2.5 px-3 py-2 text-left hover:bg-black/5 dark:hover:bg-white/10 transition"
      style={{ color: danger ? "#FF4E00" : undefined }}
    >
      <Icon className="h-4 w-4" />
      <span className="font-medium">{label}</span>
    </button>
  );
}