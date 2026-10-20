"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { LogOut, MoreVertical } from "lucide-react";
import type { AccountLink } from "./account";

export interface ShellUser {
  email: string;
  tenantName?: string | null;
  /** Nombre a mostrar; sin él se muestra el correo. */
  displayName?: string | null;
  /** Rol a mostrar; si falta, la barra lo deduce de los roles (`suiteRoleLabel`). */
  roleLabel?: string;
}

const itemClass =
  "flex w-full items-center gap-2.5 px-3 py-2 text-left transition hover:bg-black/5 dark:hover:bg-white/10";

// Cuenta en la barra común, con el diseño de siempre de Trámites: rol, empresa y nombre, avatar y el menú ⋮.
// Salir va a /auth/logout de la app (@flit/auth), que cierra también la sesión del hub y la de los demás productos de
// este navegador (A-13). Una app que cierra sesión por su cuenta (la sesión antigua de Trámites, B-13) pasa `onLogout`.
export function AccountMenu({
  user,
  links = [],
  logoutHref = "/auth/logout",
  onLogout,
}: {
  user: ShellUser;
  links?: AccountLink[];
  logoutHref?: string;
  onLogout?: () => void;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const name = user.displayName || user.email || "—";
  const initial = (user.displayName?.[0] ?? user.email?.[0] ?? "U").toUpperCase();

  useEffect(() => {
    if (!open) return;
    const onPointer = (e: PointerEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    window.addEventListener("pointerdown", onPointer);
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("pointerdown", onPointer);
      window.removeEventListener("keydown", onKey);
    };
  }, [open]);

  return (
    <>
      <div className="hidden flex-col items-end leading-tight sm:flex">
        <span className="text-[10px] font-medium text-flit-brand">{user.roleLabel || "—"}</span>
        {user.tenantName && <span className="text-[10px] opacity-55">{user.tenantName}</span>}
        <span className="text-xs font-semibold">{name}</span>
      </div>
      <div
        className="grid h-9 w-9 select-none place-items-center rounded-full border-2 text-xs font-bold text-white"
        style={{
          borderColor: "var(--color-flit-tech)",
          background: "linear-gradient(135deg,var(--color-flit-brand),var(--color-flit-tech))",
        }}
        aria-label="Avatar"
      >
        {initial}
      </div>
      <div ref={ref} className="relative">
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          aria-label="Menú de usuario"
          aria-expanded={open}
          aria-controls="flit-suite-account"
          className="rounded-md p-1 hover:bg-black/5 dark:hover:bg-white/10"
        >
          <MoreVertical className="h-5 w-5" />
        </button>
        {open && (
          <div
            id="flit-suite-account"
            role="region"
            aria-label="Cuenta"
            className="absolute right-0 top-full z-50 mt-2 w-60 rounded-xl border border-[var(--color-flit-gray)] bg-white py-1.5 text-xs text-[var(--color-flit-primary)] shadow-[0_18px_40px_-10px_rgba(22,39,68,0.25)] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
          >
            {links.map(({ label, href, icon: Icon }) => (
              <Link key={href} href={href} onClick={() => setOpen(false)} className={itemClass}>
                <Icon className="h-4 w-4" aria-hidden="true" />
                <span className="font-medium">{label}</span>
              </Link>
            ))}
            {links.length > 0 && <div className="my-1 h-px bg-[var(--color-flit-gray)] dark:bg-white/10" />}
            {onLogout ? (
              <button
                type="button"
                onClick={() => {
                  setOpen(false);
                  onLogout();
                }}
                className={itemClass}
                style={{ color: "var(--color-flit-alert)" }}
              >
                <LogOut className="h-4 w-4" aria-hidden="true" />
                <span className="font-medium">Salir de la plataforma</span>
              </button>
            ) : (
              <a href={logoutHref} className={itemClass} style={{ color: "var(--color-flit-alert)" }}>
                <LogOut className="h-4 w-4" aria-hidden="true" />
                <span className="font-medium">Salir de la plataforma</span>
              </a>
            )}
          </div>
        )}
      </div>
    </>
  );
}
