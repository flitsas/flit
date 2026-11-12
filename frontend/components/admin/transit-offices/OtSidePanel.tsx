"use client";

import { Icono } from "@flit/ui/Icono";
import type { ReactNode } from "react";
import { Modal, type ModalSize } from "@/components/atom/Modal";

/**
 * Antes era un panel lateral con overlay. Ahora se muestra como el modal normal de los demás módulos
 * (`@flit/ui` Modal), centrado y ajustado al alto de su contenido. Se conserva el nombre y la API para no
 * tocar a los consumidores (reglas, etiquetas, webhooks, guía de documentos…).
 */
export interface OtSidePanelProps {
  open: boolean;
  title: string;
  /** Nombre accesible del diálogo (los lectores de pantalla y las pruebas lo usan). */
  ariaLabel: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  disabled?: boolean;
  /** Clase de z-index del overlay (p. ej. cuando hay otro modal encima). */
  zClassName?: string;
  /** Ancho del modal: `md` formularios cortos, `lg` densos, `xl` / `2xl` fichas con grid. */
  width?: "md" | "lg" | "xl" | "2xl";
  /** `modal` usa el fondo claro del prototipo FLIT (`#EEF5FF`). */
  surface?: "card" | "modal";
  /**
   * Scroll vertical del cuerpo. `false` para contenidos que maquetan su propio alto (p. ej. la guía
   * informativa de documentos, que reparte el contenido en una grilla sin desbordar).
   */
  scrollable?: boolean;
}

const SIZE: Record<NonNullable<OtSidePanelProps["width"]>, ModalSize> = {
  md: "md",
  lg: "lg",
  xl: "xl",
  "2xl": "2xl",
};

export function OtSidePanel({
  open,
  title,
  ariaLabel,
  onClose,
  children,
  footer,
  disabled = false,
  zClassName = "z-50",
  width = "md",
  surface = "card",
  scrollable = true,
}: OtSidePanelProps) {
  return (
    <Modal
      open={open}
      title={title}
      onClose={onClose}
      busy={disabled}
      size={SIZE[width]}
      zClassName={zClassName}
      panelClassName={surface === "modal" ? "!bg-secondary" : ""}
      bodyClassName={scrollable ? "" : "!overflow-y-hidden"}
      header={({ titleId }) => (
        <div className="relative flex flex-col items-center px-10 text-center">
          {/* El nombre accesible del diálogo es `ariaLabel`; el texto visible es `title`. */}
          <h2 id={titleId} aria-label={ariaLabel} className="text-[1.375rem] font-bold leading-tight text-[color:var(--di-heading)]">
            {title}
          </h2>
          <button
            type="button"
            aria-label="Cerrar"
            onClick={onClose}
            disabled={disabled}
            className="absolute -right-2 -top-2 grid h-9 w-9 place-items-center rounded-full bg-transparent text-[color:var(--di-text-tertiary)] transition hover:bg-[color:var(--kit-hover)] hover:text-[color:var(--di-action)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)] disabled:opacity-50"
          >
            <Icono nombre="cerrar" size={20} />
          </button>
        </div>
      )}
      footer={footer}
    >
      {children}
    </Modal>
  );
}
