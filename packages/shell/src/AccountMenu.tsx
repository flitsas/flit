"use client";

import { useEffect, useRef, useState } from "react";
import { LogOut } from "lucide-react";

export interface ShellUser {
  email: string;
  tenantName: string;
  /** Rol a mostrar (el primero del producto). */
  roleLabel?: string;
}

// Menú de cuenta de la barra común (B-10). Cerrar sesión va a /auth/logout de la app (@flit/auth), que cierra también
// la sesión del hub y la de los demás productos de este navegador (A-13).
export function AccountMenu({ user, logoutHref = "/auth/logout" }: { user: ShellUser; logoutHref?: string }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const initial = user.email.charAt(0).toUpperCase() || "?";

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
          <span className="block max-w-[14rem] truncate text-xs font-semibold text-[var(--nav-texto-fuerte)]">{user.email}</span>
          <span className="block max-w-[14rem] truncate text-[11px] text-[var(--nav-texto)]">{user.tenantName}</span>
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
            <p className="truncate text-sm font-semibold text-[var(--nav-texto-fuerte)]">{user.email}</p>
            <p className="truncate text-xs text-[var(--nav-texto)]">{user.tenantName}</p>
            {user.roleLabel && <p className="truncate text-xs text-[var(--nav-texto)]">{user.roleLabel}</p>}
          </div>
          <a
            href={logoutHref}
            className="mt-1 flex items-center gap-2 rounded-full px-3 py-2 text-sm text-[var(--nav-texto)] transition-colors hover:bg-[var(--nav-app-bg)] hover:text-[var(--nav-texto-fuerte)]"
          >
            <LogOut className="h-4 w-4" aria-hidden="true" />
            Cerrar sesión
          </a>
        </div>
      )}
    </div>
  );
}
