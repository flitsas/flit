'use client';

import { useEffect, useId, useRef, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { DETALLE_OVERLAY_STYLE, DETALLE_SHEET_CLASS } from './detalle-visual';

/**
 * Shell del modal Detalle — NO es el átomo Modal blanco (anti-patrón spec flit-detalle-tramite).
 * Canvas #EEF5FF directamente sobre overlay navy blur(6px).
 *
 * HU #12195 — gestión de foco WCAG 2.1 AA (2.4.3 orden del foco / 2.1.2 sin trampa de teclado):
 * al abrir se recuerda el elemento disparador y se lleva el foco dentro del diálogo; mientras está
 * abierto el Tab circula solo entre sus elementos enfocables; al cerrar el foco vuelve al
 * disparador. Sin esto, quien abre el detalle desde una fila de tabla por teclado quedaba con el
 * foco en `document.body` y, al cerrar, tenía que recorrer la página entera para volver a su sitio.
 */
const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',');

export function DetalleTramiteShell({
  open,
  onClose,
  title,
  header,
  children,
  busy = false,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  header?: (ctx: { titleId: string }) => ReactNode;
  children: ReactNode;
  busy?: boolean;
}) {
  const titleId = useId();
  const sheetRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !busy) {
        onClose();
        return;
      }
      if (e.key !== 'Tab') return;
      const sheet = sheetRef.current;
      if (!sheet) return;
      // Sin filtro por `offsetParent`: en jsdom siempre es null y dejaría la lista vacía. Basta con
      // descartar lo explícitamente oculto, que es lo único que el selector no cubre ya.
      const focusables = Array.from(
        sheet.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      ).filter((el) => !el.hasAttribute('hidden') && el.getAttribute('aria-hidden') !== 'true');
      if (focusables.length === 0) {
        e.preventDefault();
        sheet.focus();
        return;
      }
      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      const active = document.activeElement as HTMLElement | null;
      if (!active || !sheet.contains(active)) {
        e.preventDefault();
        (e.shiftKey ? last : first).focus();
        return;
      }
      if (e.shiftKey && active === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && active === last) {
        e.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open, busy, onClose]);

  // Foco inicial dentro del diálogo y devolución al disparador al cerrar/desmontar. El disparador se
  // lee al principio de este mismo efecto: es el último instante en que `document.activeElement`
  // sigue siendo quien abrió el modal (la línea siguiente ya mueve el foco al diálogo).
  useEffect(() => {
    if (!open) return;
    const active = document.activeElement;
    const trigger = active instanceof HTMLElement && active !== document.body ? active : null;
    const sheet = sheetRef.current;
    const target = sheet?.querySelector<HTMLElement>(FOCUSABLE_SELECTOR) ?? sheet ?? null;
    target?.focus();
    return () => {
      if (trigger && document.contains(trigger)) trigger.focus();
    };
  }, [open]);

  if (!open || typeof document === 'undefined') return null;

  const requestClose = () => {
    if (!busy) onClose();
  };

  return createPortal(
    <div
      className="fixed inset-0 z-[1100] flex items-center justify-center p-4"
      style={DETALLE_OVERLAY_STYLE}
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) requestClose();
      }}
    >
      <div
        ref={sheetRef}
        tabIndex={-1}
        className={DETALLE_SHEET_CLASS}
        onMouseDown={(e) => e.stopPropagation()}
      >
        {header ? (
          header({ titleId })
        ) : (
          <h2 id={titleId} className="sr-only">
            {title}
          </h2>
        )}
        {children}
      </div>
    </div>,
    document.body,
  );
}
