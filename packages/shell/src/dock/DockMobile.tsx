"use client";

import { useEffect, useRef, useState, type RefObject } from "react";
import Link from "next/link";
import { X } from "lucide-react";
import type { DockEntry, DockGroup } from "../nav";

// Navegación de la suite en móvil y tableta (<lg), donde el dock horizontal no cabe (HU #10844, GUIA-DOCK §9): un
// lanzador redondo que abre una hoja con los mismos grupos del dock. Portado del Shell de Trámites de develop.

type Props = {
  groups: DockGroup[];
  homeIconSrc: string;
  /** Contenedor con scroll (layout `app`): el lanzador queda dentro de él; sin él, fijo en la ventana. */
  scrollRef?: RefObject<HTMLElement | null>;
};

/** Las entradas con hijos se aplanan: en la hoja no hay paneles desplegables. */
function flatten(entries: DockEntry[]): DockEntry[] {
  return entries.flatMap((it) => (it.children?.length ? it.children : [it]));
}

export function DockMobile({ groups, homeIconSrc, scrollRef }: Props) {
  const [open, setOpen] = useState(false);
  const launcherRef = useRef<HTMLButtonElement>(null);
  const position = scrollRef ? "absolute" : "fixed";

  const close = () => {
    setOpen(false);
    launcherRef.current?.focus();
  };

  // Escape cierra y devuelve el foco al lanzador (GUIA-DOCK §9).
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setOpen(false);
      launcherRef.current?.focus();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open]);

  if (groups.length === 0) return null;

  return (
    <div className="lg:hidden">
      <button
        ref={launcherRef}
        type="button"
        onClick={() => setOpen(true)}
        className={`pointer-events-auto ${position} bottom-5 left-1/2 z-40 h-14 w-14 -translate-x-1/2 overflow-hidden rounded-full focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)] focus-visible:ring-offset-2`}
        style={{ boxShadow: "var(--nav-sombra-activo)" }}
        aria-label="Abrir menú de navegación"
        aria-expanded={open}
        aria-controls="dock-mobile-sheet"
      >
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src={homeIconSrc} alt="" aria-hidden="true" className="h-full w-full object-cover" />
      </button>

      {open && (
        <div
          className={`${position} inset-0 z-50 flex items-end justify-center p-4`}
          style={{ background: "rgba(22, 39, 68, 0.45)", backdropFilter: "blur(4px)" }}
          onPointerDown={(e) => {
            if (e.target === e.currentTarget) close();
          }}
          role="dialog"
          aria-modal="true"
          aria-label="Navegación"
        >
          <div
            id="dock-mobile-sheet"
            className="dock-sheet max-h-[min(70vh,32rem)] w-full max-w-sm overflow-y-auto rounded-[var(--nav-radio-panel)] p-4"
            style={{
              background: "var(--nav-panel-bg)",
              border: "1px solid var(--nav-borde)",
              boxShadow: "var(--nav-sombra-panel)",
            }}
          >
            <div className="mb-3 flex items-center justify-between">
              <span className="text-xs font-semibold uppercase tracking-wide text-[var(--nav-texto-tenue)]">Navegación</span>
              <button
                type="button"
                onClick={close}
                className="rounded-md p-1 hover:bg-[var(--nav-app-bg)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)]"
                aria-label="Cerrar menú"
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </button>
            </div>
            <div className="flex flex-col gap-3">
              {groups.map((g) => (
                <div key={g.id}>
                  <p className="px-1 pb-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--nav-texto-tenue)]">
                    {g.label}
                  </p>
                  <div className="grid grid-cols-4 gap-2 sm:grid-cols-5">
                    {flatten(g.items).map((it) => {
                      const Icon = it.icon;
                      return (
                        <Link
                          key={it.key}
                          href={it.href}
                          onClick={() => setOpen(false)}
                          className={`dock-pill flex flex-col items-center gap-1 rounded-xl p-2 text-center transition-colors duration-[var(--nav-duracion)] ease-[var(--nav-ease)] motion-reduce:transition-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--nav-focus)] ${
                            it.active ? "font-semibold text-white" : "font-medium"
                          }`}
                          style={
                            it.active
                              ? { background: "var(--nav-activo)", boxShadow: "var(--nav-sombra-activo)", color: "#ffffff" }
                              : undefined
                          }
                          aria-current={it.active ? "page" : undefined}
                        >
                          <Icon className="h-5 w-5" strokeWidth={it.active ? 2.4 : 1.8} aria-hidden="true" />
                          <span className="line-clamp-2 text-[10px] leading-tight">{it.label}</span>
                        </Link>
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
