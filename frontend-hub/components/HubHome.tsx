"use client";

import { createElement, useEffect, useMemo, useState } from "react";
import { ArrowRight, ArrowUpRight } from "lucide-react";
import { reauthenticate } from "@flit/auth/client";
import { AppsSessionError, fetchMyApps, type SuiteApp } from "@flit/shell/apps";
import { buildDock } from "@flit/shell/nav";
import { BRAND_GRADIENT, ProductIcon } from "@/components/ui/ProductIcon";
import { StatusChip } from "@/components/ui/StatusChip";
import { productInfo } from "@/lib/products";
import { hubNav, type HubUser } from "./HubShell";

const card =
  "rounded-2xl border border-[var(--color-flit-gray)] bg-white shadow-[var(--nav-sombra-dock)] dark:border-white/10 dark:bg-[#0B0F14]";
const muted = "text-[#59677d] dark:text-white/65";

/** Fecha de hoy en Colombia («jueves, 2 de octubre»), la misma en el servidor y en el navegador. */
function today(): string {
  const text = new Intl.DateTimeFormat("es-CO", { weekday: "long", day: "numeric", month: "long", timeZone: "America/Bogota" }).format(new Date());
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/**
 * Inicio del hub con sesión (B-11, opción 4): saludo, una tarjeta por producto que el usuario puede abrir y los
 * accesos de administración que le correspondan. Si el servidor no pudo leer los productos, se piden aquí por el
 * proxy (que renueva el token). Si la sesión del hub ya no sirve (se cerró desde otro producto, o entró otro usuario),
 * se pide una nueva en silencio en vez de mostrar «sin productos» con el usuario anterior.
 */
export function HubHome({ user, tramitesUrl, apps: initial }: { user: HubUser; tramitesUrl: string; apps: SuiteApp[] | null }) {
  const [apps, setApps] = useState<SuiteApp[] | null>(initial);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (apps) return;
    const controller = new AbortController();
    fetchMyApps(controller.signal)
      .then(setApps)
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        if (error instanceof AppsSessionError && reauthenticate({ returnTo: "/?inicio=1", silent: true })) return;
        setFailed(true);
      });
    return () => controller.abort();
  }, [apps]);

  const shortcuts = useMemo(
    () => buildDock(hubNav(tramitesUrl), user, "/").flatMap((g) => g.items),
    [tramitesUrl, user],
  );
  const products = (apps ?? []).filter((app) => app.code !== "plataforma");

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-10 py-2">
      <section className="relative overflow-hidden rounded-3xl px-6 py-8 text-white md:px-10 md:py-10" style={{ background: BRAND_GRADIENT }}>
        <div aria-hidden="true" className="pointer-events-none absolute -right-16 -top-24 h-64 w-64 rounded-full bg-white/10" />
        <div aria-hidden="true" className="pointer-events-none absolute -bottom-28 right-40 h-56 w-56 rounded-full bg-white/10" />
        <p className="relative text-sm font-medium text-white/85">{today()}</p>
        <h1 className="relative mt-2 text-2xl font-semibold md:text-3xl">Hola, {user.email}</h1>
        <p className="relative mt-2 text-sm text-white/90">
          {user.tenantName}
          {user.tenantName && " · "}
          Elige el producto con el que quieres trabajar hoy.
        </p>
      </section>

      <section aria-labelledby="hub-productos">
        <h2 id="hub-productos" className="text-lg font-semibold">
          Tus productos
        </h2>
        {failed ? (
          <p role="alert" className={`mt-4 text-sm ${muted}`}>
            No pudimos cargar tus productos.{" "}
            <button type="button" onClick={() => window.location.reload()} className="font-semibold text-flit-brand underline-offset-4 hover:underline">
              Intentar de nuevo
            </button>
          </p>
        ) : apps === null ? (
          <ul className="mt-4 grid gap-5 sm:grid-cols-2 lg:grid-cols-3" aria-busy="true" aria-label="Cargando productos">
            {[0, 1, 2].map((i) => (
              <li key={i} className={`${card} h-44 animate-pulse`} />
            ))}
          </ul>
        ) : products.length === 0 ? (
          <p className={`mt-4 text-sm ${muted}`}>Tu empresa todavía no tiene productos habilitados.</p>
        ) : (
          <ul className="mt-4 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {products.map((app) => {
              const info = productInfo(app.code);
              const soon = Boolean(app.comingSoon);
              return (
                <li key={app.code}>
                  <a
                    href={app.url}
                    className={`${card} group flex h-full flex-col p-6 transition hover:-translate-y-0.5 hover:border-flit-brand focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)]`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <ProductIcon icon={app.icon} muted={soon} />
                      <StatusChip status={soon ? "soon" : "available"} />
                    </div>
                    <p className="mt-5 text-lg font-semibold">{app.name}</p>
                    {info && <p className={`mt-1.5 flex-1 text-sm leading-relaxed ${muted}`}>{info.tagline}</p>}
                    <span className="mt-5 inline-flex items-center gap-1 text-sm font-semibold text-flit-brand">
                      {soon ? "Ver qué viene" : `Abrir ${app.name}`}
                      <ArrowRight className="h-4 w-4 transition group-hover:translate-x-0.5" aria-hidden="true" />
                    </span>
                  </a>
                </li>
              );
            })}
          </ul>
        )}
      </section>

      {shortcuts.length > 0 && (
        <section aria-labelledby="hub-admin">
          <h2 id="hub-admin" className="text-lg font-semibold">
            Administración
          </h2>
          <ul className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {shortcuts.map((item) => (
              <li key={item.key}>
                <a
                  href={item.href}
                  className={`${card} group flex items-center gap-3 px-4 py-3.5 text-sm font-medium transition hover:border-flit-brand`}
                >
                  <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-flit-brand/10 text-flit-brand">
                    {createElement(item.icon, { className: "h-4 w-4", "aria-hidden": true })}
                  </span>
                  <span className="flex-1">{item.label}</span>
                  <ArrowUpRight className="h-4 w-4 text-[#59677d] transition group-hover:text-flit-brand dark:text-white/50" aria-hidden="true" />
                </a>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
