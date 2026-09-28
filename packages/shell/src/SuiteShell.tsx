"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Menu, X } from "lucide-react";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { useBrand } from "@flit/brand/BrandProvider";
import { AccountMenu, type ShellUser } from "./AccountMenu";
import type { SuiteApp } from "./apps";
import { Dock } from "./dock/Dock";
import { buildDock, type NavCatalog, type NavViewer } from "./nav";
import { ProductMenu } from "./ProductMenu";

export type { NavCatalog, NavItem, NavSection, NavViewer } from "./nav";
export type { ShellUser } from "./AccountMenu";
export type { SuiteApp } from "./apps";

export interface SuiteShellProps {
  /** Código del producto (contrato §1): resalta su tarjeta en el menú de productos. */
  productCode: string;
  /** Nombre visible junto a la marca. */
  productName: string;
  /** Catálogo de navegación del producto (Contrato A de la guía del dock). */
  nav: NavCatalog;
  /** Usuario y permisos de la sesión del producto (`@flit/auth` SessionUser). */
  user: ShellUser & NavViewer;
  /** Productos ya cargados (p. ej. en el servidor); si faltan, el menú los pide al abrirse. */
  apps?: SuiteApp[];
  /** Destino del botón central del dock. */
  homeHref?: string;
  /** Icono del botón central del dock (cada app lo sirve desde su public/). */
  homeIconSrc?: string;
  children: React.ReactNode;
}

/**
 * Barra común de la FLIT Suite (B-10, contrato §8): marca del host, nombre del producto, menú de productos, menú de
 * cuenta y dock del producto. No importa nada de un producto concreto: todo lo que cambia entre productos entra por
 * props (catálogo, usuario, nombre).
 */
export function SuiteShell({
  productCode,
  productName,
  nav,
  user,
  apps,
  homeHref = "/",
  homeIconSrc = "/assets/favicon.svg",
  children,
}: SuiteShellProps) {
  const pathname = usePathname() ?? "/";
  const brand = useBrand();
  const [mobileOpen, setMobileOpen] = useState(false);
  const { permissions, isSuperAdmin, roles } = user;
  const groups = useMemo(() => buildDock(nav, { permissions, isSuperAdmin, roles }, pathname), [nav, permissions, isSuperAdmin, roles, pathname]);

  return (
    <div className="min-h-screen bg-[var(--nav-app-bg)]">
      <header className="sticky top-0 z-40 border-b border-[var(--nav-borde)] bg-white/90 backdrop-blur">
        <div className="mx-auto flex h-14 max-w-screen-2xl items-center gap-3 px-4">
          <button
            type="button"
            className="flex h-9 w-9 items-center justify-center rounded-full text-[var(--nav-texto)] hover:bg-[var(--nav-app-bg)] lg:hidden"
            aria-label={mobileOpen ? "Cerrar menú" : "Abrir menú"}
            aria-expanded={mobileOpen}
            aria-controls="flit-suite-mobile-nav"
            onClick={() => setMobileOpen((v) => !v)}
          >
            {mobileOpen ? <X className="h-5 w-5" aria-hidden="true" /> : <Menu className="h-5 w-5" aria-hidden="true" />}
          </button>
          <Link href={homeHref} className="flex items-center gap-3" aria-label={`${brand.platformName} — ${productName}`}>
            <BrandLogo variant="dark" className="h-7 w-auto" />
            <span className="hidden border-l border-[var(--nav-borde)] pl-3 text-sm font-semibold text-[var(--nav-texto-fuerte)] sm:inline">
              {productName}
            </span>
          </Link>
          <div className="ml-auto flex items-center gap-1">
            <ProductMenu productCode={productCode} apps={apps} />
            <AccountMenu user={user} />
          </div>
        </div>
        {mobileOpen && (
          <nav id="flit-suite-mobile-nav" aria-label="Navegación principal" className="border-t border-[var(--nav-borde)] px-4 py-3 lg:hidden">
            <ul className="flex flex-col gap-3">
              {groups.map((g) => (
                <li key={g.id}>
                  <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-[var(--nav-texto)]">{g.label}</p>
                  <ul className="flex flex-col">
                    {g.items.flatMap((it) => [it, ...(it.children ?? [])]).map((it) => (
                      <li key={it.key}>
                        <Link
                          href={it.href}
                          onClick={() => setMobileOpen(false)}
                          aria-current={it.active ? "page" : undefined}
                          className={`block rounded-lg px-2 py-2 text-sm ${it.active ? "font-semibold text-flit-brand" : "text-[var(--nav-texto-fuerte)]"}`}
                        >
                          {it.label}
                        </Link>
                      </li>
                    ))}
                  </ul>
                </li>
              ))}
            </ul>
          </nav>
        )}
      </header>

      {/* Colchón para que el dock fijo no tape el final del contenido (guía §4.3). */}
      <main className="mx-auto max-w-screen-2xl px-4 pb-28 pt-6">{children}</main>

      <Dock groups={groups} homeHref={homeHref} homeLabel={`Inicio ${productName}`} homeIconSrc={homeIconSrc} homeActive={pathname === homeHref} />
    </div>
  );
}
