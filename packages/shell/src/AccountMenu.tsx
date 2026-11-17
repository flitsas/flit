"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { LogOut } from "lucide-react";
import type { AccountLink } from "./account";
import { ProductEntries } from "./ProductMenu";
import type { SuiteAppsState } from "./useSuiteApps";

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
// HU #14562 · D1: con `products`, el menú trae el grupo «Tus productos» (`li.suite-solo-movil`), que el CSS solo muestra
// a ≤ 640 px, donde el botón ▦ sale de la barra (patrón `solo-movil` de la propuesta UI Kit 2026).
export function AccountMenu({
  user,
  links = [],
  logoutHref = "/auth/logout",
  onLogout,
  products,
}: {
  user: ShellUser;
  links?: AccountLink[];
  logoutHref?: string;
  onLogout?: () => void;
  products?: { state: SuiteAppsState; productCode: string };
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const request = products?.state.request;

  useEffect(() => {
    if (open) request?.();
  }, [open, request]);
  const name = user.displayName || user.email || "—";
  const initials = initialsOf(user);

  useEffect(() => {
    if (!open) return;
    const onPointer = (e: PointerEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setOpen(false);
      // El panel desaparece con el foco dentro: sin esto, el foco cae en <body>.
      buttonRef.current?.focus();
    };
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
    <div ref={ref} className="suite-ancla">
      <button
        ref={buttonRef}
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
          {(products || links.length > 0) && (
            <ul>
              {products && (
                <li className="suite-solo-movil">
                  <div role="group" aria-label="Tus productos" className="suite-menu-usuario__productos">
                    <ProductEntries
                      productCode={products.productCode}
                      apps={products.state.apps}
                      failed={products.state.failed}
                      onNavigate={() => setOpen(false)}
                    />
                  </div>
                </li>
              )}
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
