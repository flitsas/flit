"use client";

import { createElement, useEffect, useRef, useState } from "react";
import { House } from "lucide-react";
import { Icono, IconoModulo } from "@flit/ui/Icono";
import { appIcon, appTagline, type SuiteApp } from "./apps";
import type { SuiteAppsState } from "./useSuiteApps";

// Menú de productos (▦, B-10/B-11): la única forma de cambiar de producto. Cada producto abre en su host; la sesión
// del hub hace que no haya que volver a iniciar sesión. Dice qué se hace ahí («¿En qué quieres trabajar hoy?»), qué es
// cada producto y en cuál está el usuario; el inicio de la suite queda al pie.
// HU #14562 · D1: la carga de `me/apps` vive en `useSuiteApps` (SuiteShell), compartida con el grupo «Tus productos» del
// menú de usuario, que es donde quedan los productos a ≤ 640 px (este botón se oculta ahí por CSS).
export function ProductMenu({ productCode, state }: { productCode: string; state: SuiteAppsState }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const { request } = state;

  useEffect(() => {
    if (open) request();
  }, [open, request]);

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

  return (
    <div ref={ref} className="suite-ancla suite-ancla--productos">
      <button
        ref={buttonRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label="Productos"
        title="Tus productos"
        aria-expanded={open}
        aria-controls="flit-suite-products"
        className="suite-icono-btn"
      >
        <Icono nombre="rejilla" size={26} />
      </button>
      {open && (
        // `.menu-flotante--productos` de la propuesta (HU #14479): mismo panel que el menú de usuario y el submenú del
        // dock; cada producto con su ilustración, nombre en negrita y para qué sirve debajo.
        <div id="flit-suite-products" role="region" aria-label="Productos" className="suite-menu-productos">
          <div className="suite-menu-productos__cabeza">
            <p className="suite-menu-productos__titulo">¿En qué quieres trabajar hoy?</p>
            <p className="suite-menu-productos__bajada">Tus productos FLIT, con tu misma cuenta.</p>
          </div>
          <ProductEntries productCode={productCode} apps={state.apps} failed={state.failed} />
        </div>
      )}
    </div>
  );
}

/**
 * Lista de productos con sus 4 estados (cargando, error, vacío, lleno) e «Ir al inicio» al pie. La usan el panel ▦ y el
 * grupo «Tus productos» del menú de usuario (interno al paquete).
 */
export function ProductEntries({
  productCode,
  apps,
  failed,
  onNavigate,
}: {
  productCode: string;
  apps: SuiteApp[] | null;
  failed: boolean;
  onNavigate?: () => void;
}) {
  const products = (apps ?? []).filter((app) => app.code !== "plataforma");
  const home = apps?.find((app) => app.code === "plataforma");

  return (
    <>
      {failed ? (
        <p className="suite-menu-productos__aviso">No pudimos cargar los productos.</p>
      ) : apps === null ? (
        <p className="suite-menu-productos__aviso">Cargando…</p>
      ) : products.length === 0 ? (
        <p className="suite-menu-productos__aviso">Tu empresa todavía no tiene productos habilitados.</p>
      ) : (
        <ul>
          {products.map((app) => {
            const current = app.code === productCode;
            const tagline = appTagline(app.code);
            return (
              <li key={app.code}>
                <a
                  href={app.url}
                  onClick={onNavigate}
                  aria-current={current ? "page" : undefined}
                  className={`group suite-menu-productos__entrada ${app.comingSoon ? "suite-menu-productos__entrada--pronto" : ""}`}
                >
                  <ProductGlyph app={app} />
                  <span className="suite-menu-productos__texto">
                    <strong>
                      {app.name}
                      {current ? (
                        <span className="suite-menu-productos__pastilla suite-menu-productos__pastilla--aqui">Estás aquí</span>
                      ) : app.comingSoon ? (
                        <span className="suite-menu-productos__pastilla">Próximamente</span>
                      ) : null}
                    </strong>
                    {tagline && <small>{tagline}</small>}
                  </span>
                  {!current && (
                    <Icono nombre="derecha" className="h-4 w-4 shrink-0 opacity-0 transition group-hover:translate-x-0.5 group-hover:opacity-100" />
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
          onClick={onNavigate}
          aria-current={home.code === productCode ? "page" : undefined}
          className="suite-menu-productos__pie"
        >
          <House className="h-4 w-4" aria-hidden="true" />
          Ir al inicio
        </a>
      )}
    </>
  );
}

/** Productos con ícono propio en el kit (MAPA_ICONO de `@flit/ui/iconos-mapas`). */
const ILUSTRADOS = new Set(["tramites", "comparendos", "diagnostico"]);

/** Ícono del producto (disco del kit); un código nuevo sin ícono cae en su ícono de línea dentro del disco. */
function ProductGlyph({ app }: { app: SuiteApp }) {
  if (ILUSTRADOS.has(app.code)) {
    return <IconoModulo clave={app.code} disco size={36} className="suite-menu-productos__ilus" />;
  }
  return (
    <span className="suite-menu-productos__ilus suite-menu-productos__ilus--icono" aria-hidden="true">
      {createElement(appIcon(app.icon), { className: "h-5 w-5", "aria-hidden": true })}
    </span>
  );
}

function withHomeFlag(url: string): string {
  return `${url.replace(/\/+$/, "")}/?inicio=1`;
}
