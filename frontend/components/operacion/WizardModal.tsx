'use client';

import { useRef, type ReactNode } from 'react';
import { Icono } from "@flit/ui/Icono";
import { useWizardFocusTrap } from './use-wizard-focus-trap';
import { DIALOGO_FRANJA, DIALOGO_FRANJA_CIRCULO, DIALOGO_FRANJA_TITULO, DIALOGO_PANEL } from './dialogo-propio';
import { MODAL_VELO_CLS } from '@flit/ui/Modal';

/**
 * Modal centrado del asistente, en la forma de la propuesta: overlay opaco de marca, tarjeta de
 * radio amplio, cierre en la esquina y título en azul de marca.
 *
 * Es el contenedor que el diseño usa para todo lo que se CONSULTA sin salir del paso —escrituras
 * de la compañía, documentos a tener listos, confirmaciones—. No es un cajón lateral: el contenido
 * es corto y se lee de una vez, y centrarlo evita el barrido de ojos hasta el borde de la pantalla.
 *
 * B5/B6 (guardián de diseño) — punto de consolidación de los diálogos del asistente: overlay
 * opaco `rgba(22,39,68,0.45)` + `backdrop-blur-[6px]` (el único desenfoque autorizado, junto al
 * del dock) sobre un contenedor SIN blur, y trampa de foco + retorno de foco + Escape vía
 * `useWizardFocusTrap`. Antes tenía dos overlays traslúcidos (`bg-white/70 backdrop-blur-md` +
 * `bg-white/90 backdrop-blur-xl`), el ejemplo textual de glassmorphism que el guardián rechaza.
 */
export function WizardModal({
  title,
  children,
  onClose,
  wide = false,
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
  /** Ancho para listados con tarjetas (escrituras); por defecto, columna corta. */
  wide?: boolean;
}) {
  const cardRef = useRef<HTMLDivElement>(null);

  useWizardFocusTrap(cardRef, { active: true, onEscape: onClose });

  return (
    <div className={`fixed inset-0 z-[1150] grid place-items-center p-6 ${MODAL_VELO_CLS}`}>
      {/* Captura del clic fuera. No es un control accesible a propósito: duplicaría el nombre del
          botón de cierre y el teclado ya sale por Escape o por la X. */}
      <div className="absolute inset-0" aria-hidden="true" onClick={onClose} />
      <div
        ref={cardRef}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`${DIALOGO_PANEL} flex max-h-[85vh] w-full ${
          wide ? 'max-w-2xl' : 'max-w-md'
        } flex-col outline-none focus:ring-0`}
      >
        {/* La X sigue antes del título en el orden de foco; se pinta sobre la franja (homologación #14507). */}
        <button
          type="button"
          onClick={onClose}
          aria-label="Cerrar"
          className="absolute right-4 top-4 z-[2] grid h-9 w-9 place-items-center rounded-full bg-transparent text-[color:var(--di-text-tertiary)] transition hover:bg-[color:var(--kit-hover)] hover:text-[color:var(--di-action)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)] sm:right-5 sm:top-5"
        >
          <Icono nombre="cerrar" className="h-5 w-5" />
        </button>
        <div className={`${DIALOGO_FRANJA} pr-16`}>
          <span aria-hidden="true" className={DIALOGO_FRANJA_CIRCULO} />
          <h3 className={DIALOGO_FRANJA_TITULO}>{title}</h3>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto p-6">{children}</div>
      </div>
    </div>
  );
}
