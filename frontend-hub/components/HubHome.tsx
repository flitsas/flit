"use client";

import { useEffect, useMemo, useState } from "react";
import { ArrowUpRight } from "lucide-react";
import { reauthenticate } from "@flit/auth/client";
import { appIcon, AppsSessionError, fetchMyApps, type SuiteApp } from "@flit/shell/apps";
import { buildDock } from "@flit/shell/nav";
import { hubNav, type HubUser } from "./HubShell";

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
    <div className="flex flex-col gap-8">
      <header>
        <h1 className="text-2xl font-semibold text-[var(--nav-texto-fuerte)]">Hola, {user.email}</h1>
        <p className="text-sm text-[var(--nav-texto)]">{user.tenantName}</p>
      </header>

      <section aria-labelledby="hub-productos">
        <h2 id="hub-productos" className="mb-3 text-sm font-semibold uppercase tracking-wide text-[var(--nav-texto)]">
          Tus productos
        </h2>
        {failed ? (
          <p role="alert" className="text-sm text-[var(--nav-texto)]">
            No pudimos cargar tus productos.{" "}
            <button type="button" onClick={() => window.location.reload()} className="font-semibold text-flit-brand underline-offset-4 hover:underline">
              Intentar de nuevo
            </button>
          </p>
        ) : apps === null ? (
          <p className="text-sm text-[var(--nav-texto)]">Cargando…</p>
        ) : products.length === 0 ? (
          <p className="text-sm text-[var(--nav-texto)]">Tu empresa todavía no tiene productos habilitados.</p>
        ) : (
          <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {products.map((app) => {
              const Icon = appIcon(app.icon);
              return (
                <li key={app.code}>
                  <a
                    href={app.url}
                    className="group flex h-full items-center gap-4 rounded-2xl border border-[var(--nav-borde)] bg-white p-5 transition hover:border-flit-brand hover:shadow-[var(--nav-sombra-dock)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)]"
                  >
                    <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-[var(--nav-app-bg)] text-flit-brand">
                      <Icon className="h-6 w-6" aria-hidden="true" />
                    </span>
                    <span className="flex-1 text-base font-semibold text-[var(--nav-texto-fuerte)]">{app.name}</span>
                    <ArrowUpRight className="h-4 w-4 text-[var(--nav-texto)] transition group-hover:text-flit-brand" aria-hidden="true" />
                  </a>
                </li>
              );
            })}
          </ul>
        )}
      </section>

      {shortcuts.length > 0 && (
        <section aria-labelledby="hub-admin">
          <h2 id="hub-admin" className="mb-3 text-sm font-semibold uppercase tracking-wide text-[var(--nav-texto)]">
            Administración
          </h2>
          <ul className="flex flex-wrap gap-2">
            {shortcuts.map((item) => {
              const Icon = item.icon;
              return (
                <li key={item.key}>
                  <a
                    href={item.href}
                    className="inline-flex items-center gap-2 rounded-full border border-[var(--nav-borde)] bg-white px-4 py-2 text-sm text-[var(--nav-texto-fuerte)] transition hover:bg-[var(--nav-app-bg)]"
                  >
                    <Icon className="h-4 w-4" aria-hidden="true" />
                    {item.label}
                  </a>
                </li>
              );
            })}
          </ul>
        </section>
      )}
    </div>
  );
}
