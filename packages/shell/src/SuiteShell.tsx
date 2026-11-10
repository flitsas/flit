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
import { DockMobile } from "./dock/DockMobile";
import { buildDock, isActive, type NavCatalog, type NavViewer } from "./nav";
import { ProductMenu } from "./ProductMenu";
import { CommandPalette } from "./CommandPalette";
import { ShellNavProvider } from "./ShellNavContext";
import type { PlateSearch, SearchAction } from "./search";
import { ThemeToggle } from "./theme";

export type { NavCatalog, NavItem, NavSection, NavViewer } from "./nav";
export type { ShellUser } from "./AccountMenu";
export type { AccountLink } from "./account";
export type { SuiteApp } from "./apps";
export type { PlateSearch, SearchAction } from "./search";

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
  /**
   * Buscador Ctrl K (HU #14480). Las secciones salen solas del catálogo filtrado; aquí van las acciones rápidas del
   * producto (rutas que ya existen) y a dónde lleva una placa. Cada una nombra la entrada del dock de la que depende.
   */
  searchActions?: SearchAction[];
  plateSearch?: PlateSearch;
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
  searchActions,
  plateSearch,
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
  const home = useMemo(() => ({ href: homeHref, label: `Inicio ${productName}` }), [homeHref, productName]);
  const shellNav = useMemo(() => ({ groups, homeHref }), [groups, homeHref]);
  const accountUser = useMemo(() => ({ ...user, roleLabel: user.roleLabel ?? suiteRoleLabel(roles ?? []) }), [user, roles]);

  // Barra superior del prototipo «Diseño Inteligente» (HU #14479, `.barra`): marca y producto a la izquierda; a la
  // derecha el buscador ancho (Ctrl K), el menú de productos, el tema y el bloque de usuario. Sin campana: la suite
  // todavía no tiene un dato real de avisos que mostrar.
  const header = (
    <header className={`suite-barra z-40 ${app ? "shrink-0" : "sticky top-0"}`}>
      <Link href={homeHref} className="suite-barra__marca" aria-label={`${brand.platformName} — ${productName}`}>
        <BrandLogo variant="dark" className="suite-barra__logo dark:hidden" />
        <BrandLogo variant="white" className="suite-barra__logo hidden dark:block" />
        <span className="suite-barra__producto">{productName}</span>
      </Link>
      <div className="suite-barra__acciones">
        <CommandPalette groups={groups} home={home} actions={searchActions} plate={plateSearch} />
        {headerActions}
        <ProductMenu productCode={productCode} apps={apps} loadApps={loadApps} />
        {themeToggle && <ThemeToggle />}
        <AccountMenu user={accountUser} links={menuLinks} onLogout={onLogout} />
      </div>
    </header>
  );

  // Escritorio (lg+): el dock. Móvil y tableta: un lanzador con los mismos grupos en una hoja.
  const dock = (
    <>
      <Dock
        groups={groups}
        homeHref={homeHref}
        homeLabel={`Inicio ${productName}`}
        homeIconSrc={homeIconSrc}
        homeActive={homeActive}
        scrollRef={app ? scrollRef : undefined}
      />
      <DockMobile groups={groups} homeIconSrc={homeIconSrc} scrollRef={app ? scrollRef : undefined} />
    </>
  );

  if (app) {
    return (
      <ShellNavProvider value={shellNav}>
      <div className="flex h-screen w-full flex-col overflow-hidden bg-[var(--di-surface)] text-[var(--color-flit-primary)] dark:text-white">
        {header}
        <main className="relative isolate min-h-0 flex-1 overflow-hidden">
          {/* Propuesta «Diseño Inteligente» (propuesta.css `.fondo`): círculo cielo abajo a la izquierda y arco fino a la
              derecha, fijos detrás del contenido. */}
          <div aria-hidden="true" className="pointer-events-none absolute inset-0 -z-10 overflow-clip">
            <span className="absolute -bottom-[220px] -left-[180px] h-[520px] w-[520px] rounded-full bg-[var(--di-sky)]" />
            <span className="absolute -right-[380px] top-[18%] h-[640px] w-[640px] rounded-full border-[1.5px] border-[var(--di-sky-line)] opacity-35" />
          </div>
          {/* Colchón inferior para que el dock no tape el final del contenido (guía §4.3). El fondo propio de las
              páginas (`.app-bg`) se apaga aquí para que se vea el de la propuesta. */}
          <div
            ref={scrollRef}
            className="absolute inset-0 overflow-y-auto pb-28 [&_.app-bg]:bg-transparent [&_.app-bg]:bg-none"
            data-shell-scroll
          >
            {/* Sin contenedor centrado: cada página conserva su propio ancho y relleno (decisión de producto, HU #14479). */}
            {children}
          </div>
          {dock}
          {overlay}
        </main>
        {footer}
      </div>
      </ShellNavProvider>
    );
  }

  return (
    <ShellNavProvider value={shellNav}>
    <div className="min-h-screen bg-[var(--color-flit-bg)] text-[var(--color-flit-primary)] dark:bg-[#05060A] dark:text-white">
      {header}
      {/* Colchón para que el dock fijo no tape el final del contenido (guía §4.3). */}
      <main className="mx-auto max-w-screen-2xl px-4 pb-28 pt-6">{children}</main>
      {dock}
      {overlay}
      {footer}
    </div>
    </ShellNavProvider>
  );
}
