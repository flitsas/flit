"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { LogOut } from "lucide-react";
import type { NavIcon } from "./nav";

export interface ShellUser {
  email: string;
  tenantName?: string | null;
  /** Nombre a mostrar; sin él se muestra el correo. */
  displayName?: string | null;
  /** Rol a mostrar (el primero del producto). */
  roleLabel?: string;
}

/** Enlace propio del producto en el menú de cuenta (p. ej. Ayuda o Cambio de contraseña en Trámites). */
export interface AccountLink {
  label: string;
  href: string;
  icon: NavIcon;
}

// Menú de cuenta de la barra común (B-10). Cerrar sesión va a /auth/logout de la app (@flit/auth), que cierra también
// la sesión del hub y la de los demás productos de este navegador (A-13). Una app que cierra sesión por su cuenta (la
// sesión antigua de Trámites, B-13) pasa `onLogout`.
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
  const name = user.displayName || user.email;
  const initial = name.charAt(0).toUpperCase() || "?";
  const itemClass =
    "mt-1 flex w-full items-center gap-2 rounded-full px-3 py-2 text-left text-sm text-[var(--nav-texto)] transition-colors hover:bg-[var(--nav-app-bg)] hover:text-[var(--nav-texto-fuerte)]";

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
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label="Menú de cuenta"
        aria-expanded={open}
        aria-controls="flit-suite-account"
        className="flex items-center gap-2 rounded-full py-1 pl-1 pr-3 transition-colors hover:bg-[var(--nav-app-bg)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)]"
      >
        <span className="flex h-8 w-8 items-center justify-center rounded-full bg-flit-brand text-sm font-semibold text-white">{initial}</span>
        <span className="hidden text-left leading-tight sm:block">
          <span className="block max-w-[14rem] truncate text-xs font-semibold text-[var(--nav-texto-fuerte)]">{name}</span>
          {user.tenantName && <span className="block max-w-[14rem] truncate text-[11px] text-[var(--nav-texto)]">{user.tenantName}</span>}
        </span>
      </button>
      {open && (
        <div
          id="flit-suite-account"
          role="region"
          aria-label="Cuenta"
          className="absolute right-0 top-full z-50 mt-2 w-64 rounded-[var(--nav-radio-panel)] border border-[var(--nav-borde)] bg-white p-2 shadow-[var(--nav-sombra-panel)]"
        >
          <div className="border-b border-[var(--nav-borde)] px-3 pb-2 pt-1">
            <p className="truncate text-sm font-semibold text-[var(--nav-texto-fuerte)]">{name}</p>
            {user.displayName && <p className="truncate text-xs text-[var(--nav-texto)]">{user.email}</p>}
            {user.tenantName && <p className="truncate text-xs text-[var(--nav-texto)]">{user.tenantName}</p>}
            {user.roleLabel && <p className="truncate text-xs text-[var(--nav-texto)]">{user.roleLabel}</p>}
          </div>
          {links.map(({ label, href, icon: Icon }) => (
            <Link key={href} href={href} onClick={() => setOpen(false)} className={itemClass}>
              <Icon className="h-4 w-4" aria-hidden="true" />
              {label}
            </Link>
          ))}
          {onLogout ? (
            <button
              type="button"
              onClick={() => {
                setOpen(false);
                onLogout();
              }}
              className={itemClass}
            >
              <LogOut className="h-4 w-4" aria-hidden="true" />
              Cerrar sesión
            </button>
          ) : (
            <a href={logoutHref} className={itemClass}>
              <LogOut className="h-4 w-4" aria-hidden="true" />
              Cerrar sesión
            </a>
          )}
        </div>
      )}
    </div>
  );
}
