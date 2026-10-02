"use client";

import { useMemo, useRef } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { BrandLogo } from "@flit/brand/BrandLogo";
import { useBrand } from "@flit/brand/BrandProvider";
import { suiteAccountLinks, suiteRoleLabel, type AccountLink } from "./account";
import { AccountMenu, type ShellUser } from "./AccountMenu";
import type { SuiteApp } from "./apps";
import { Dock } from "./dock/Dock";
import { buildDock, isActive, type NavCatalog, type NavViewer } from "./nav";
import { ProductMenu } from "./ProductMenu";
import { ThemeToggle } from "./theme";

export type { NavCatalog, NavItem, NavSection, NavViewer } from "./nav";
export type { ShellUser } from "./AccountMenu";
export type { AccountLink } from "./account";
export type { SuiteApp } from "./apps";

const NO_LINKS: AccountLink[] = [];

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
  /**
   * Origen donde viven las opciones de cuenta de la suite (Ayuda, Cambio de contraseña): vacío si están en esta app
   * (Trámites); la URL de Trámites desde cualquier otra, hasta que B-12 las lleve al hub.
   */
  accountUrl?: string;
  /** Opciones propias del producto en el menú de usuario, después de las de la suite. */
  accountLinks?: AccountLink[];
  /** Cierre de sesión propio de la app; sin él, Cerrar sesión va a /auth/logout (@flit/auth). */
  onLogout?: () => void;
  /** Interruptor claro/oscuro de la suite (por defecto, sí). */
  themeToggle?: boolean;
  /** Controles propios del producto en la barra, antes del menú de productos. */
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
  accountUrl = "",
  accountLinks = NO_LINKS,
  onLogout,
  themeToggle = true,
  headerActions,
  overlay,
  footer,
  layout = "page",
  children,
}: SuiteShellProps) {
  const pathname = usePathname() ?? "/";
  const brand = useBrand();
  const scrollRef = useRef<HTMLDivElement>(null);
  const { permissions, isSuperAdmin, roles, modules } = user;
  const groups = useMemo(
    () => buildDock(nav, { permissions, isSuperAdmin, roles, modules }, pathname, search),
    [nav, permissions, isSuperAdmin, roles, modules, pathname, search],
  );
  // Inicio está activo en su ruta mientras ninguna entrada del dock lo esté (`/` y `/?m=reportes` comparten ruta).
  const homeActive = isActive(homeHref.split("?")[0], pathname) && !groups.some((g) => g.active);
  const app = layout === "app";
  const menuLinks = useMemo(() => [...suiteAccountLinks(accountUrl), ...accountLinks], [accountUrl, accountLinks]);
  const accountUser = useMemo(() => ({ ...user, roleLabel: user.roleLabel ?? suiteRoleLabel(roles ?? []) }), [user, roles]);

  // La barra de siempre de Trámites (logo, tema, rol/empresa/nombre, avatar y ⋮), con lo que agrega la suite: el nombre
  // del producto junto al logo y el menú de productos (▦).
  const header = (
    <header
      className={`z-40 flex items-center justify-between border-b border-[var(--color-flit-gray)] px-4 py-3 md:px-6 dark:border-white/[0.08] ${
        app ? "shrink-0" : "sticky top-0 bg-[var(--color-flit-bg)]/90 backdrop-blur dark:bg-[#05060A]/90"
      }`}
    >
      <Link href={homeHref} className="flex items-center gap-3" aria-label={`${brand.platformName} — ${productName}`}>
        <BrandLogo variant="dark" className="h-10 w-auto dark:hidden" />
        <BrandLogo variant="white" className="hidden h-10 w-auto dark:block" />
        <span className="hidden border-l border-[var(--color-flit-gray)] pl-3 text-sm font-semibold sm:inline dark:border-white/10">
          {productName}
        </span>
      </Link>
      <div className="flex items-center gap-3">
        {themeToggle && <ThemeToggle />}
        {headerActions}
        <ProductMenu productCode={productCode} apps={apps} loadApps={loadApps} />
        <AccountMenu user={accountUser} links={menuLinks} onLogout={onLogout} />
      </div>
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
    <div className="min-h-screen bg-[var(--color-flit-bg)] text-[var(--color-flit-primary)] dark:bg-[#05060A] dark:text-white">
      {header}
      {/* Colchón para que el dock fijo no tape el final del contenido (guía §4.3). */}
      <main className="mx-auto max-w-screen-2xl px-4 pb-28 pt-6">{children}</main>
      {dock}
      {overlay}
      {footer}
    </div>
  );
}
