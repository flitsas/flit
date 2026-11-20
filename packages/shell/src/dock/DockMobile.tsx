"use client";

import { useEffect, useRef, useState, type RefObject } from "react";
import { Icono } from "@flit/ui/Icono";
import type { DockEntry, DockGroup } from "../nav";
import { DockEntryLink } from "./Dock";
import { DockGlyph } from "./DockGlyph";
import { useDockHomeMark } from "./DockHomeMark";

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
  const home = useDockHomeMark(homeIconSrc);

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
        className={`dock-fab dock-fab--lanzador pointer-events-auto ${position} bottom-5 left-1/2 z-40 -translate-x-1/2 ${home.shape === "pill" ? "dock-fab--pastilla" : ""}`}
        aria-label="Abrir menú de navegación"
        aria-expanded={open}
        aria-controls="dock-mobile-sheet"
      >
        {home.content}
      </button>

      {open && (
        <div
          className={`${position} inset-0 z-50 flex items-end justify-center p-2`}
          style={{ background: "var(--overlay, rgba(22, 39, 68, 0.45))", backdropFilter: "blur(4px)" }}
          onPointerDown={(e) => {
            if (e.target === e.currentTarget) close();
          }}
          role="dialog"
          aria-modal="true"
          aria-label="Navegación"
        >
          {/* Mismo lenguaje que el submenú del dock (HU #14479): franja tinta arriba, ilustración en el título de cada grupo y entradas con viñeta. */}
          <div id="dock-mobile-sheet" className="dock-menu dock-menu--hoja dock-sheet">
            <div className="dock-menu__cabeza">
              <span className="dock-menu__cabeza-texto">
                <strong>Navegación</strong>
              </span>
              <button type="button" onClick={close} className="dock-menu__cerrar" aria-label="Cerrar menú">
                <Icono nombre="cerrar" className="h-4 w-4" />
              </button>
            </div>
            <div className="dock-menu__lista">
              {groups.map((g) => {
                const titleId = `dock-mobile-${g.id}`;
                return (
                  <div key={g.id} role="group" aria-labelledby={titleId} className="dock-menu__seccion">
                    <p id={titleId} className="dock-menu__seccion-titulo">
                      <DockGlyph icon={g.icon} ilustracion={g.ilustracion} className="dock-menu__seccion-glifo" />
                      {g.label}
                    </p>
                    <ul className="dock-menu__entradas">
                      {flatten(g.items).map((it) => (
                        <li key={it.key}>
                          <DockEntryLink item={it} onNavigate={() => setOpen(false)} />
                        </li>
                      ))}
                    </ul>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
