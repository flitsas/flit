"use client";

import type { ReactNode } from "react";
import { cn } from "./cn";
import { Icono } from "./Icono";
import { Modal, MODAL_TITULO_CLS } from "./Modal";

/**
 * Detalle largo en el modal de la suite (ola Diagnóstico UX 2026-11-15 · FE-1, spec-ui §1.4–§1.6). Sustituye en la práctica
 * a `PanelLateral`: mismas props (sin `ancho`) y la misma distribución (título y descripción · acciones · «Cerrar» ·
 * cuerpo con scroll · pie fijo), pero centrado sobre el velo del `Modal` con `size="panel"` (640 px exactos).
 *
 * Accesibilidad: lo da el `Modal` (role="dialog", aria-modal, aria-labelledby, `useTrampaFoco` con pila de capas, foco
 * inicial `[data-autofocus]` → primer campo → título, y devolución del foco al disparador). `cerrarConVelo={false}` en los
 * modales con campos editables para no perder lo escrito; con `ocupado` no se cierra por ninguna vía.
 */
export interface PanelModalProps {
  abierto: boolean;
  onCerrar: () => void;
  titulo: string;
  descripcion?: ReactNode;
  /** Acciones de la cabecera junto a «Cerrar» (p. ej. la bandera de saneamiento). */
  acciones?: ReactNode;
  /** Pie fijo (fuera del scroll). */
  pie?: ReactNode;
  /** Ocupado: Escape, el velo y «Cerrar» quedan inertes. */
  ocupado?: boolean;
  /** Clic en el velo cierra. `true` por defecto; `false` en modales con campos editables. */
  cerrarConVelo?: boolean;
  etiquetaCerrar?: string;
  className?: string;
  children: ReactNode;
}

export function PanelModal({
  abierto,
  onCerrar,
  titulo,
  descripcion,
  acciones,
  pie,
  ocupado = false,
  cerrarConVelo = true,
  etiquetaCerrar = "Cerrar",
  className,
  children,
}: PanelModalProps) {
  const pedirCierre = () => {
    if (!ocupado) onCerrar();
  };

  return (
    <Modal
      open={abierto}
      onClose={onCerrar}
      title={titulo}
      size="panel"
      busy={ocupado}
      closeOnBackdrop={cerrarConVelo}
      panelClassName={cn(
        "min-h-[min(24rem,calc(100dvh-3rem))]",
        "opacity-100 transition-opacity duration-[var(--di-motion-base)] ease-[var(--di-ease)] starting:opacity-0 motion-reduce:transition-none",
        className,
      )}
      bodyClassName="py-1"
      header={({ titleId }) => (
        <div className="flex items-start gap-3 border-b border-[color:var(--di-divider)] pb-4">
          <div className="min-w-0 flex-1">
            {/* tabIndex=-1: sin campos en el cuerpo, el foco inicial del Modal cae en el título. */}
            <h2 id={titleId} tabIndex={-1} className={cn(MODAL_TITULO_CLS, "outline-none")}>
              {titulo}
            </h2>
            {descripcion ? <p className="mt-1 text-sm text-[color:var(--di-text-tertiary)]">{descripcion}</p> : null}
          </div>
          {acciones}
          <button
            type="button"
            onClick={pedirCierre}
            aria-disabled={ocupado || undefined}
            className="inline-flex min-h-9 items-center gap-1.5 rounded-full px-3 text-sm font-semibold text-[color:var(--di-action)] underline underline-offset-[3px] hover:bg-[color:var(--kit-hover)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)] aria-disabled:cursor-not-allowed aria-disabled:opacity-60"
          >
            <Icono nombre="cerrar" size={16} />
            {etiquetaCerrar}
          </button>
        </div>
      )}
      footer={pie}
    >
      {children}
    </Modal>
  );
}
