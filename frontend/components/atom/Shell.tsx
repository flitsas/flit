"use client";

import { ReactNode, useEffect, useRef, useState } from "react";

const logoWhite = "/assets/logo-flit-white.svg";
const logoDark = "/assets/logo-flit-dark.svg";
const iso = "/assets/iso-flit.svg";
import {
  LayoutGrid,
  FileStack,
  BarChart3,
  ShieldCheck,
  Users,
  HelpCircle,
  Building2,
  Bell,
  Sun,
  Moon,
  MoreVertical,
  UserCog,
  KeyRound,
  LogOut,
} from "lucide-react";

export type ModuleId =
  | "dashboard"
  | "tramites"
  | "reportes"
  | "validaciones"
  | "usuarios"
  | "ayuda";

const DOCK: { id: ModuleId; label: string; icon: typeof LayoutGrid }[] = [
  { id: "dashboard", label: "Dashboard", icon: LayoutGrid },
  { id: "tramites", label: "Trámites", icon: FileStack },
  { id: "reportes", label: "Reportes", icon: BarChart3 },
  { id: "validaciones", label: "Validaciones", icon: ShieldCheck },
  { id: "usuarios", label: "Usuarios y Permisos", icon: Users },
  { id: "ayuda", label: "Ayuda", icon: HelpCircle },
];

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

export function Shell({
  children,
  active,
  onNav,
  onLogout,
}: {
  children: ReactNode;
  active: ModuleId;
  onNav: (v: ModuleId) => void;
  onLogout?: () => void;
}) {
  const { dark, toggle } = useTheme();
  const logoSrc = dark ? logoWhite : logoDark;

  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const h = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    document.addEventListener("mousedown", h);
    return () => document.removeEventListener("mousedown", h);
  }, []);

  // Split dock into left (3) and right (3) around the FAB
  const left = DOCK.slice(0, 3);
  const right = DOCK.slice(3);

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
        className="shrink-0 flex items-center justify-between px-6 py-3 border-b"
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
          <button className="relative p-2 rounded-xl" aria-label="Notificaciones">
            <Bell className="h-5 w-5" />
            <span className="absolute top-1 right-1 h-4 w-4 text-[9px] font-bold rounded-full grid place-items-center text-white" style={{ background: "#FF4E00" }}>1</span>
          </button>
          <div className="hidden sm:flex flex-col items-end leading-tight">
            <span className="text-[10px] font-medium" style={{ color: "#557EFF" }}>Súper Admin / Tenant</span>
            <span className="text-xs font-semibold">Mateo Ruiz Gil</span>
          </div>
          <img
            src="https://i.pravatar.cc/80?img=12"
            alt="Mateo Ruiz Gil"
            className="h-9 w-9 rounded-full object-cover border-2"
            style={{ borderColor: "#00DBD5" }}
          />
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
                <MenuItem icon={UserCog} label="Actualización de la información" onClick={() => setMenuOpen(false)} />
                <MenuItem icon={KeyRound} label="Cambio de contraseña" onClick={() => setMenuOpen(false)} />
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
        <div className="absolute inset-0 overflow-hidden">{children}</div>

        {/* Bottom dock */}
        <div className="absolute left-1/2 -translate-x-1/2 bottom-5 z-40">
          <div
            className="flex items-center gap-1 px-3 py-2 rounded-full"
            style={{
              background: dark ? "rgba(11,15,20,0.85)" : "rgba(255,255,255,0.92)",
              boxShadow: "0 10px 40px -10px rgba(22,39,68,0.35), 0 4px 14px rgba(0,0,0,0.08)",
              backdropFilter: "blur(20px)",
              border: `1px solid ${dark ? "#1A1F2B" : "#DFE5ED"}`,
            }}
          >
            {left.map((it) => (
              <DockBtn key={it.id} item={it} active={active === it.id} onClick={() => onNav(it.id)} dark={dark} />
            ))}
            {/* FAB */}
            <button
              onClick={() => onNav("dashboard")}
              className="mx-2 h-14 w-14 rounded-full grid place-items-center transition-transform hover:scale-105"
              style={{
                background: "linear-gradient(135deg,#557EFF 0%,#00DBD5 100%)",
                boxShadow: "0 10px 24px -6px rgba(85,126,255,0.55)",
              }}
              aria-label="Inicio FLIT"
            >
              <img src={iso} alt="FLIT" className="h-7 w-7 brightness-0 invert" />
            </button>
            {right.map((it) => (
              <DockBtn key={it.id} item={it} active={active === it.id} onClick={() => onNav(it.id)} dark={dark} />
            ))}
            {/* Consola de Administración de Compañías (ruta aparte, gate SuperAdmin). */}
            <DockBtn
              item={{ label: "Compañías", icon: Building2 }}
              active={false}
              onClick={() => {
                window.location.href = "/admin/companies";
              }}
              dark={dark}
            />
          </div>
        </div>
      </main>

      {/* Footer */}
      <footer
        className="shrink-0 px-6 py-2 border-t text-[10px] text-center"
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

function DockBtn({
  item,
  active,
  onClick,
  dark,
}: {
  item: { label: string; icon: typeof LayoutGrid };
  active: boolean;
  onClick: () => void;
  dark: boolean;
}) {
  const Icon = item.icon;
  return (
    <button
      onClick={onClick}
      className="group relative h-11 w-11 rounded-full grid place-items-center transition"
      style={{
        background: active ? (dark ? "rgba(0,219,213,0.18)" : "rgba(85,126,255,0.12)") : "transparent",
        color: active ? "#557EFF" : dark ? "#FFFFFF" : "#162744",
      }}
      aria-label={item.label}
    >
      <Icon className="h-5 w-5" strokeWidth={active ? 2.4 : 1.8} />
      {active && (
        <span className="absolute -bottom-1 h-1 w-1 rounded-full" style={{ background: "#557EFF" }} />
      )}
      <span
        className="pointer-events-none absolute bottom-full mb-2 left-1/2 -translate-x-1/2 whitespace-nowrap text-[10px] font-medium px-2 py-1 rounded-md opacity-0 group-hover:opacity-100 transition"
        style={{ background: "#162744", color: "#FFFFFF" }}
      >
        {item.label}
      </span>
    </button>
  );
}