"use client";

import { createElement, useEffect, useRef, useState } from "react";
import { ArrowRight, Grid3x3, House } from "lucide-react";
import { appIcon, appTagline, fetchMyApps, type SuiteApp } from "./apps";

// Menú de productos (▦, B-10/B-11): la única forma de cambiar de producto. Cada producto abre en su host; la sesión
// del hub hace que no haya que volver a iniciar sesión. Dice qué se hace ahí («¿En qué quieres trabajar hoy?»), qué es
// cada producto y en cuál está el usuario; el inicio de la suite queda al pie.
export function ProductMenu({
  productCode,
  apps: given,
  loadApps = fetchMyApps,
}: {
  productCode: string;
  apps?: SuiteApp[];
  /** Cómo pedir `me/apps` si no vienen cargados; por defecto, el proxy /api/v1 de la app con su cookie. */
  loadApps?: (signal: AbortSignal) => Promise<SuiteApp[]>;
}) {
  const [open, setOpen] = useState(false);
  const [apps, setApps] = useState<SuiteApp[] | null>(given ?? null);
  const [failed, setFailed] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open || apps || failed) return;
    const controller = new AbortController();
    loadApps(controller.signal)
      .then(setApps)
      .catch(() => !controller.signal.aborted && setFailed(true));
    return () => controller.abort();
  }, [open, apps, failed, loadApps]);

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

  const products = (apps ?? []).filter((app) => app.code !== "plataforma");
  const home = apps?.find((app) => app.code === "plataforma");

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label="Productos"
        title="Tus productos"
        aria-expanded={open}
        aria-controls="flit-suite-products"
        className="flex h-9 w-9 items-center justify-center rounded-full text-[var(--nav-texto)] transition-colors hover:bg-[var(--nav-app-bg)] hover:text-[var(--nav-texto-fuerte)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)]"
      >
        <Grid3x3 className="h-5 w-5" aria-hidden="true" />
      </button>
      {open && (
        <div
          id="flit-suite-products"
          role="region"
          aria-label="Productos"
          className="absolute right-0 top-full z-50 mt-2 w-[22rem] max-w-[calc(100vw-2rem)] overflow-hidden rounded-[var(--nav-radio-panel)] border border-[var(--nav-borde)] bg-white shadow-[var(--nav-sombra-panel)] dark:border-white/10 dark:bg-[#0B0F14]"
        >
          <div className="border-b border-[var(--nav-borde)] px-4 pb-3 pt-4 dark:border-white/10">
            <p className="text-sm font-semibold text-[var(--nav-texto-fuerte)] dark:text-white">¿En qué quieres trabajar hoy?</p>
            <p className="mt-0.5 text-xs text-[var(--nav-texto)] dark:text-white/60">Tus productos FLIT, con tu misma cuenta.</p>
          </div>
          {failed ? (
            <p className="px-4 py-6 text-center text-sm text-[var(--nav-texto)] dark:text-white/60">No pudimos cargar los productos.</p>
          ) : apps === null ? (
            <p className="px-4 py-6 text-center text-sm text-[var(--nav-texto)] dark:text-white/60">Cargando…</p>
          ) : products.length === 0 ? (
            <p className="px-4 py-6 text-center text-sm text-[var(--nav-texto)] dark:text-white/60">Tu empresa todavía no tiene productos habilitados.</p>
          ) : (
            <ul className="p-2">
              {products.map((app) => {
                const current = app.code === productCode;
                const tagline = appTagline(app.code);
                return (
                  <li key={app.code}>
                    <a
                      href={app.url}
                      aria-current={current ? "page" : undefined}
                      className={`group flex items-center gap-3 rounded-xl px-2.5 py-2.5 transition-colors hover:bg-[var(--nav-app-bg)] ${
                        current ? "bg-[var(--nav-app-bg)]" : ""
                      }`}
                    >
                      <span
                        className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl ${
                          app.comingSoon ? "bg-[var(--nav-app-bg)] text-flit-brand" : "text-white"
                        }`}
                        style={app.comingSoon ? undefined : { background: "linear-gradient(120deg,#00dbd5 0%,#557eff 100%)" }}
                      >
                        {createElement(appIcon(app.icon), { className: "h-5 w-5", "aria-hidden": true })}
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="flex items-center gap-2 text-sm font-semibold text-[var(--nav-texto-fuerte)] dark:text-white">
                          {app.name}
                          {current ? (
                            <span className="rounded-full bg-flit-brand/10 px-2 py-0.5 text-[10px] font-semibold text-flit-brand">Estás aquí</span>
                          ) : app.comingSoon ? (
                            <span className="rounded-full bg-[var(--nav-app-bg)] px-2 py-0.5 text-[10px] font-semibold text-[var(--nav-texto)] dark:text-white/60">
                              Próximamente
                            </span>
                          ) : null}
                        </span>
                        {tagline && <span className="mt-0.5 block text-xs leading-snug text-[var(--nav-texto)] dark:text-white/60">{tagline}</span>}
                      </span>
                      {!current && (
                        <ArrowRight
                          className="h-4 w-4 shrink-0 text-[var(--nav-texto)] opacity-0 transition group-hover:translate-x-0.5 group-hover:opacity-100"
                          aria-hidden="true"
                        />
                      )}
                    </a>
                  </li>
                );
              })}
            </ul>
          )}
          {home && (
            <a
              // El inicio del hub con ?inicio=1: sin él, quien tiene un solo producto entraría directo a ese producto
              // (B-11, opción 4) y no podría volver al hub.
              href={withHomeFlag(home.url)}
              aria-current={home.code === productCode ? "page" : undefined}
              className="flex items-center gap-2 border-t border-[var(--nav-borde)] px-4 py-3 text-sm font-medium text-flit-brand transition-colors hover:bg-[var(--nav-app-bg)] dark:border-white/10"
            >
              <House className="h-4 w-4" aria-hidden="true" />
              Ir al inicio
            </a>
          )}
        </div>
      )}
    </div>
  );
}

function withHomeFlag(url: string): string {
  return `${url.replace(/\/+$/, "")}/?inicio=1`;
}
