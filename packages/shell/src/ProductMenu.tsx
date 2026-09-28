"use client";

import { useEffect, useRef, useState } from "react";
import { Grid3x3 } from "lucide-react";
import { appIcon, fetchMyApps, type SuiteApp } from "./apps";

// Menú de productos (▦, B-10/B-11): la única forma de cambiar de producto. Cada producto abre en su host; la sesión
// del hub hace que no haya que volver a iniciar sesión.
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
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open || apps) return;
    const controller = new AbortController();
    loadApps(controller.signal).then(setApps).catch(() => setApps([]));
    return () => controller.abort();
  }, [open, apps, loadApps]);

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

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label="Productos"
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
          className="absolute right-0 top-full z-50 mt-2 w-72 rounded-[var(--nav-radio-panel)] border border-[var(--nav-borde)] bg-white p-3 shadow-[var(--nav-sombra-panel)]"
        >
          {apps === null ? (
            <p className="px-2 py-4 text-center text-sm text-slate-500">Cargando…</p>
          ) : apps.length === 0 ? (
            <p className="px-2 py-4 text-center text-sm text-slate-500">No hay productos disponibles.</p>
          ) : (
            <ul className="grid grid-cols-3 gap-1">
              {apps.map((app) => {
                const Icon = appIcon(app.icon);
                const current = app.code === productCode;
                return (
                  <li key={app.code}>
                    <a
                      // El inicio del hub con ?inicio=1: sin él, quien tiene un solo producto entraría directo a ese
                      // producto (B-11, opción 4) y no podría volver al hub.
                      href={app.code === "plataforma" ? withHomeFlag(app.url) : app.url}
                      aria-current={current ? "page" : undefined}
                      className={`flex flex-col items-center gap-1.5 rounded-xl px-2 py-3 text-center text-xs transition-colors hover:bg-[var(--nav-app-bg)] ${
                        current ? "font-semibold text-flit-brand" : "text-[var(--nav-texto-fuerte)]"
                      }`}
                    >
                      <Icon className="h-6 w-6" aria-hidden="true" />
                      <span className="truncate">{app.code === "plataforma" ? "Inicio" : app.name}</span>
                    </a>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function withHomeFlag(url: string): string {
  return `${url.replace(/\/+$/, "")}/?inicio=1`;
}
