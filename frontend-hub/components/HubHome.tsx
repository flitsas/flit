"use client";

import { createElement, useEffect, useMemo, useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { reauthenticate } from "@flit/auth/client";
import { AppsSessionError, fetchMyApps, type SuiteApp } from "@flit/shell/apps";
import { buildDock } from "@flit/shell/nav";
import { ProductArt } from "@/components/ui/ProductIcon";
import { StatusChip } from "@/components/ui/StatusChip";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { productInfo } from "@/lib/products";
import { hubNav, type HubUser } from "./HubShell";

// `.card` de la propuesta «Diseño Inteligente» (HU #14487): superficie elevada, borde suave, radio de 20 px y sombra.
const card =
  "rounded-[20px] border border-[color:var(--di-border)] bg-[color:var(--di-surface-elevated)] shadow-[var(--shadow-card)]";
const muted = "text-[color:var(--di-text-secondary)]";
const subtitulo = "text-lg font-semibold text-[color:var(--di-heading)]";

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
      {/* Cabecera de módulo de la propuesta (HU #14487), con la ilustración de Inicio y la fecha como antetítulo. */}
      <CabeceraModulo
        as="section"
        antetitulo={today()}
        titulo={<>Hola, {user.email}</>}
        descripcion={
          <>
            {user.tenantName}
            {user.tenantName && " · "}
            Elige el producto con el que quieres trabajar hoy.
          </>
        }
        icono="inicio"
      />

      <section aria-labelledby="hub-productos">
        <h2 id="hub-productos" className={subtitulo}>
          Tus productos
        </h2>
        {failed ? (
          <p role="alert" className={`mt-4 text-sm ${muted}`}>
            No pudimos cargar tus productos.{" "}
            <button type="button" onClick={() => window.location.reload()} className="font-semibold text-[color:var(--di-action)] underline-offset-4 hover:underline">
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
                    className={`${card} group flex h-full flex-col p-6 transition hover:-translate-y-0.5 hover:border-[color:var(--di-border-strong)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <ProductArt code={app.code} icon={app.icon} muted={soon} />
                      <StatusChip status={soon ? "soon" : "available"} />
                    </div>
                    <p className="mt-4 text-lg font-semibold text-[color:var(--di-heading)]">{app.name}</p>
                    {info && <p className={`mt-1.5 flex-1 text-sm leading-relaxed ${muted}`}>{info.tagline}</p>}
                    <span className="mt-5 inline-flex items-center gap-1 text-sm font-semibold text-[color:var(--di-action)]">
                      {soon ? "Ver qué viene" : `Abrir ${app.name}`}
                      <Icono nombre="derecha" className="h-4 w-4 transition group-hover:translate-x-0.5" />
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
          <h2 id="hub-admin" className={subtitulo}>
            Administración
          </h2>
          <ul className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {shortcuts.map((item) => {
              const ilustracion = item.ilustracion;
              return (
                <li key={item.key}>
                  {/* `.card` de la propuesta con la ilustración del módulo (la misma del dock), nombre y acento al pasar. */}
                  <a
                    href={item.href}
                    className={`${card} group flex min-h-[72px] items-center gap-3.5 p-4 text-sm font-semibold text-[color:var(--di-heading)] transition hover:border-[color:var(--di-border-strong)] hover:bg-[color:var(--di-accent-soft)]`}
                  >
                    {ilustracion ? (
                      // eslint-disable-next-line @next/next/no-img-element -- ilustración decorativa estática
                      <img src={ilustracion} alt="" aria-hidden="true" className="h-10 w-10 shrink-0 object-contain" />
                    ) : (
                      <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[color:var(--di-accent-soft)] text-[color:var(--di-action)]">
                        {createElement(item.icon, { className: "h-4 w-4", "aria-hidden": true })}
                      </span>
                    )}
                    <span className="flex-1">{item.label}</span>
                    <Icono nombre="externo" className="h-4 w-4 text-[color:var(--di-text-tertiary)] transition group-hover:text-[color:var(--di-action)]" />
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
