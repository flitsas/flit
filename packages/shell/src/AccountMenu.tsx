"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { LogOut } from "lucide-react";
import type { AccountLink } from "./account";

export interface ShellUser {
  email: string;
  tenantName?: string | null;
  /** Nombre a mostrar; sin él se muestra el correo. */
  displayName?: string | null;
  /** Rol a mostrar; si falta, la barra lo deduce de los roles (`suiteRoleLabel`). */
  roleLabel?: string;
}

/** Iniciales del avatar: las de las dos primeras palabras del nombre o, sin nombre, la primera letra del correo. */
function initialsOf(user: ShellUser): string {
  const words = (user.displayName ?? "").trim().split(/\s+/).filter(Boolean);
  const fromName = words.slice(0, 2).map((w) => w[0]).join("");
  return (fromName || user.email?.[0] || "U").toUpperCase();
}

// Cuenta en la barra común, como el bloque de usuario del prototipo «Diseño Inteligente» (HU #14479): avatar, nombre y
// «rol · empresa» en un solo botón que abre el menú (franja tinta con la persona, opciones y Salir aparte).
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
  const initials = initialsOf(user);

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

  // Rol y empresa en piezas aparte (se leen por separado) unidas con «·», como en el prototipo.
  const detail = (
    <>
      <span>{user.roleLabel || "—"}</span>
      {user.tenantName && (
        <>
          {" · "}
          <span>{user.tenantName}</span>
        </>
      )}
    </>
  );

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label="Menú de usuario"
        aria-expanded={open}
        aria-controls="flit-suite-account"
        className="suite-usuario"
      >
        <span className="suite-usuario__avatar" aria-label="Avatar">
          {initials}
        </span>
        <span className="suite-usuario__texto">
          <strong>{name}</strong>
          <small>{detail}</small>
        </span>
      </button>
      {open && (
        <div id="flit-suite-account" role="region" aria-label="Cuenta" className="suite-menu-usuario">
          <div className="suite-menu-usuario__cabeza" aria-hidden="true">
            <span className="suite-menu-usuario__avatar">{initials}</span>
            <span>
              <strong>{name}</strong>
              <small>{detail}</small>
            </span>
          </div>
          {links.length > 0 && (
            <ul>
              {links.map(({ label, href, icon: Icon }) => (
                <li key={href}>
                  <Link href={href} onClick={() => setOpen(false)} className="suite-menu-usuario__entrada">
                    <Icon className="h-5 w-5 shrink-0" aria-hidden="true" />
                    <span>{label}</span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
          <div className="suite-menu-usuario__pie">
            {onLogout ? (
              <button
                type="button"
                onClick={() => {
                  setOpen(false);
                  onLogout();
                }}
                className="suite-menu-usuario__salir"
              >
                <LogOut className="h-4 w-4" aria-hidden="true" />
                <span>Salir de la plataforma</span>
              </button>
            ) : (
              <a href={logoutHref} className="suite-menu-usuario__salir">
                <LogOut className="h-4 w-4" aria-hidden="true" />
                <span>Salir de la plataforma</span>
              </a>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
