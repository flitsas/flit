"use client";

import { useMemo, useRef, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Menu, X } from "lucide-react";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { useBrand } from "@flit/brand/BrandProvider";
import { AccountMenu, type AccountLink, type ShellUser } from "./AccountMenu";
import type { SuiteApp } from "./apps";
import { Dock } from "./dock/Dock";
import { buildDock, isActive, type NavCatalog, type NavViewer } from "./nav";
import { ProductMenu } from "./ProductMenu";

export type { NavCatalog, NavItem, NavSection, NavViewer } from "./nav";
export type { AccountLink, ShellUser } from "./AccountMenu";
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
  /** Cómo pedir los productos si no vienen en `apps` (por defecto, el proxy /api/v1 de la app). */
  loadApps?: (signal: AbortSignal) => Promise<SuiteApp[]>;
  /** Destino del botón central del dock. */
  homeHref?: string;
  /** Icono del botón central del dock (cada app lo sirve desde su public/). */
  homeIconSrc?: string;
  /** Consulta actual (`?m=…`) para las entradas que la usan; `usePathname` no la trae. */
  search?: string;
  /** Enlaces propios del producto en el menú de cuenta, antes de Cerrar sesión. */
  accountLinks?: AccountLink[];
  /** Cierre de sesión propio de la app; sin él, Cerrar sesión va a /auth/logout (@flit/auth). */
  onLogout?: () => void;
  /** Controles propios del producto en la barra, antes del menú de productos (p. ej. el tema). */
  headerActions?: React.ReactNode;
  /** Capa flotante sobre el contenido (p. ej. un asistente). */
  overlay?: React.ReactNode;
  footer?: React.ReactNode;
  /**
   * `page`: la página hace scroll y el contenido va centrado (el hub).
   * `app`: la barra y el dock quedan fijos y el scroll es del contenido, en `[data-shell-scroll]`, sin márgenes
   * propios (Trámites, cuyas pantallas ya los traen y usan ese contenedor para lo pegajoso).
   */
  layout?: "page" | "app";
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
  loadApps,
  homeHref = "/",
  homeIconSrc = "/assets/favicon.svg",
  search = "",
  accountLinks,
  onLogout,
  headerActions,
  overlay,
  footer,
  layout = "page",
  children,
}: SuiteShellProps) {
  const pathname = usePathname() ?? "/";
  const brand = useBrand();
  const [mobileOpen, setMobileOpen] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const { permissions, isSuperAdmin, roles, modules } = user;
  const groups = useMemo(
    () => buildDock(nav, { permissions, isSuperAdmin, roles, modules }, pathname, search),
    [nav, permissions, isSuperAdmin, roles, modules, pathname, search],
  );
  // Inicio está activo en su ruta mientras ninguna entrada del dock lo esté (`/` y `/?m=reportes` comparten ruta).
  const homeActive = isActive(homeHref.split("?")[0], pathname) && !groups.some((g) => g.active);
  const app = layout === "app";

  const header = (
    <header
      className={`z-40 border-b border-[var(--nav-borde)] bg-white/90 backdrop-blur ${
        app ? "shrink-0 dark:border-white/10 dark:bg-[#05060A]/90" : "sticky top-0"
      }`}
    >
      <div className={`flex h-14 items-center gap-3 px-4 ${app ? "md:px-6" : "mx-auto max-w-screen-2xl"}`}>
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
          <BrandLogo variant="dark" className={`h-7 w-auto ${app ? "dark:hidden" : ""}`} />
          {app && <BrandLogo variant="white" className="hidden h-7 w-auto dark:block" />}
          <span className="hidden border-l border-[var(--nav-borde)] pl-3 text-sm font-semibold text-[var(--nav-texto-fuerte)] sm:inline dark:text-white">
            {productName}
          </span>
        </Link>
        <div className="ml-auto flex items-center gap-1">
          {headerActions}
          <ProductMenu productCode={productCode} apps={apps} loadApps={loadApps} />
          <AccountMenu user={user} links={accountLinks} onLogout={onLogout} />
        </div>
      </div>
      {mobileOpen && (
        <nav id="flit-suite-mobile-nav" aria-label="Navegación móvil" className="border-t border-[var(--nav-borde)] px-4 py-3 lg:hidden">
          <ul className="flex flex-col gap-3">
            {groups.map((g) => (
              <li key={g.id}>
                <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-[var(--nav-texto)]">{g.label}</p>
                <ul className="flex flex-col">
                  {g.items.flatMap((it) => (it.children?.length ? it.children : [it])).map((it) => (
                    <li key={it.key}>
                      <Link
                        href={it.href}
                        onClick={() => setMobileOpen(false)}
                        aria-current={it.active ? "page" : undefined}
                        className={`block rounded-lg px-2 py-2 text-sm ${it.active ? "font-semibold text-flit-brand" : "text-[var(--nav-texto-fuerte)] dark:text-white"}`}
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
  );

  const dock = (
    <Dock
      groups={groups}
      homeHref={homeHref}
      homeLabel={`Inicio ${productName}`}
      homeIconSrc={homeIconSrc}
      homeActive={homeActive}
      scrollRef={app ? scrollRef : undefined}
    />
  );

  if (app) {
    return (
      <div className="flex h-screen w-full flex-col overflow-hidden bg-[var(--color-flit-bg)] text-[var(--color-flit-primary)] dark:bg-[#05060A] dark:text-white">
        {header}
        <main className="relative min-h-0 flex-1 overflow-hidden">
          {/* Colchón inferior para que el dock no tape el final del contenido (guía §4.3). */}
          <div ref={scrollRef} className="absolute inset-0 overflow-y-auto pb-28" data-shell-scroll>
            {children}
          </div>
          {dock}
          {overlay}
        </main>
        {footer}
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-[var(--nav-app-bg)]">
      {header}
      {/* Colchón para que el dock fijo no tape el final del contenido (guía §4.3). */}
      <main className="mx-auto max-w-screen-2xl px-4 pb-28 pt-6">{children}</main>
      {dock}
      {overlay}
      {footer}
    </div>
  );
}
