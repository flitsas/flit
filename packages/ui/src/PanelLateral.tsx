"use client";

import { useId, useRef, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { cn } from "./cn";
import { Icono } from "./Icono";
import { MODAL_TITULO_CLS, MODAL_VELO_CLS } from "./Modal";
import { useTrampaFoco } from "./useTrampaFoco";

/**
 * Panel lateral (drawer) del Manual UI Kit 2026 (HU #14562 · UI-1): se abre a la derecha sobre el velo del Modal, con
 * 640 px de ancho por defecto, para leer un detalle largo sin perder la pantalla de fondo (p. ej. el dictamen).
 *
 * Accesibilidad (AC8): role="dialog" + aria-modal + título accesible; el foco entra al abrir, queda atrapado con Tab y
 * Shift+Tab, Escape o «Cerrar» lo cierran y el foco vuelve al control que lo abrió. Aparece por opacidad (sin escalas)
 * y sin transición con «menos movimiento». Solo usa tokens de @flit/ui; el aspecto propio entra por `className`.
 * HU #14562 · m-6: la trampa es `useTrampaFoco` (la misma del Modal), con pila de capas: un Modal abierto encima recibe
 * Tab y Escape, y el panel no los atiende hasta que el Modal se cierra.
 */
export interface PanelLateralProps {
  abierto: boolean;
  onCerrar: () => void;
  titulo: string;
  descripcion?: ReactNode;
  /** Ancho máximo en px. 640 por defecto. */
  ancho?: number;
  /** Acciones de la cabecera junto a «Cerrar» (p. ej. Descargar PDF). */
  acciones?: ReactNode;
  /** Pie fijo (fuera del scroll). */
  pie?: ReactNode;
  /** Ocupado: Escape, el velo y «Cerrar» quedan inertes. */
  ocupado?: boolean;
  etiquetaCerrar?: string;
  className?: string;
  children: ReactNode;
}

export function PanelLateral({
  abierto,
  onCerrar,
  titulo,
  descripcion,
  ancho = 640,
  acciones,
  pie,
  ocupado = false,
  etiquetaCerrar = "Cerrar",
  className,
  children,
}: PanelLateralProps) {
  const tituloId = useId();
  const panelRef = useRef<HTMLDivElement>(null);

  // Al abrir, el foco va al primer control del panel; Escape cierra salvo que esté ocupado.
  useTrampaFoco({
    activa: abierto,
    contenedor: panelRef,
    onEscape: () => {
      if (!ocupado) onCerrar();
    },
  });

  if (!abierto || typeof document === "undefined") return null;

  const pedirCierre = () => {
    if (!ocupado) onCerrar();
  };

  return createPortal(
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={tituloId}
      className={cn("fixed inset-0 z-[100] flex justify-end", MODAL_VELO_CLS)}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) pedirCierre();
      }}
      onClick={(e) => e.stopPropagation()}
    >
      <div
        ref={panelRef}
        tabIndex={-1}
        data-parte="panel"
        style={{ maxWidth: `${ancho}px` }}
        className={cn(
          "flex h-full w-full flex-col border-l border-[color:var(--di-border)] bg-card text-flit-primary shadow-none dark:text-foreground",
          "opacity-100 transition-opacity duration-[var(--di-motion-base)] ease-[var(--di-ease)] starting:opacity-0 motion-reduce:transition-none",
          "focus:outline-none",
          className,
        )}
      >
        <div className="flex shrink-0 items-start gap-3 border-b border-[color:var(--di-divider)] px-6 py-4">
          <div className="min-w-0 flex-1">
            <h2 id={tituloId} className={MODAL_TITULO_CLS}>
              {titulo}
            </h2>
            {descripcion ? <p className="mt-1 text-sm text-[color:var(--di-text-tertiary)]">{descripcion}</p> : null}
          </div>
          {acciones}
          <button
            type="button"
            onClick={pedirCierre}
            className="inline-flex min-h-9 items-center gap-1.5 rounded-full px-3 text-sm font-semibold text-[color:var(--di-action)] underline underline-offset-[3px] hover:bg-[color:var(--kit-hover)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)]"
          >
            <Icono nombre="cerrar" size={16} />
            {etiquetaCerrar}
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-6 py-4">{children}</div>
        {pie ? <div className="shrink-0 border-t border-[color:var(--di-divider)] px-6 py-4">{pie}</div> : null}
      </div>
    </div>,
    document.body,
  );
}
