'use client';

import { useRef, type ReactNode } from 'react';
import { X } from 'lucide-react';
import { useWizardFocusTrap } from './use-wizard-focus-trap';

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
    <div className="fixed inset-0 z-[1150] grid place-items-center bg-[rgba(22,39,68,0.45)] p-6 backdrop-blur-[6px]">
      {/* Captura del clic fuera. No es un control accesible a propósito: duplicaría el nombre del
          botón de cierre y el teclado ya sale por Escape o por la X. */}
      <div className="absolute inset-0" aria-hidden="true" onClick={onClose} />
      <div
        ref={cardRef}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`relative max-h-[85vh] w-full ${
          wide ? 'max-w-2xl' : 'max-w-md'
        } overflow-y-auto rounded-2xl border bg-white p-6 outline-none focus:ring-0 dark:bg-[#162744]`}
        style={{ boxShadow: '0 8px 40px -8px rgba(85,126,255,0.28)' }}
      >
        <button
          type="button"
          onClick={onClose}
          aria-label="Cerrar"
          className="absolute right-4 top-4 rounded-lg p-1 opacity-60 transition hover:opacity-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
        >
          <X className="h-4 w-4" aria-hidden="true" />
        </button>
        <h3 className="pr-6 text-base font-bold" style={{ color: '#557EFF' }}>
          {title}
        </h3>
        <div className="mt-3">{children}</div>
      </div>
    </div>
  );
}
