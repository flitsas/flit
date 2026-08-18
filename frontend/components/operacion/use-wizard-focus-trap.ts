'use client';

import { useEffect, useRef, type RefObject } from 'react';

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
  'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Trampa de foco para diálogos (guardián de diseño, bloqueante B5): atrapa Tab/Shift+Tab dentro
 * del contenedor, cierra con Escape y devuelve el foco al elemento que abrió el diálogo cuando se
 * desmonta.
 *
 * Nace de la trampa que `ActorsForm` (`EmailReenvioConfirmModal`) ya hacía a mano — Tab/Shift+Tab
 * entre Cancelar/Continuar — a la que le faltaba justamente el retorno de foco al cerrar. Se
 * comparte entre `WizardModal` y los diálogos que no migran a él por tener una composición propia
 * (icono, cabecera con buscador, contenido inline no-overlay).
 */
export function useWizardFocusTrap<T extends HTMLElement>(
  containerRef: RefObject<T | null>,
  options: {
    /** Solo atrapa/enfoca mientras el diálogo está montado/abierto. */
    active: boolean;
    /** Se invoca en Escape. Normalmente el mismo `onClose` del diálogo. */
    onEscape?: () => void;
    /** Foco inicial explícito (p. ej. el botón "Cancelar"); si no se da, usa el primer focusable. */
    initialFocusRef?: RefObject<HTMLElement | null>;
  },
) {
  const { active, onEscape, initialFocusRef } = options;
  const triggerRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!active) return;
    // Quién tenía el foco antes de abrir el diálogo: es a quien se lo devolvemos al cerrar.
    triggerRef.current = document.activeElement as HTMLElement | null;

    const getFocusables = () =>
      containerRef.current
        ? Array.from(containerRef.current.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR))
        : [];

    const initial = initialFocusRef?.current ?? getFocusables()[0] ?? containerRef.current;
    initial?.focus();

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onEscape?.();
        return;
      }
      if (e.key !== 'Tab') return;
      const items = getFocusables();
      if (items.length === 0) {
        e.preventDefault();
        return;
      }
      const first = items[0];
      const last = items[items.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      // Devuelve el foco al disparador: sin esto el teclado se queda "colgado" en el body.
      triggerRef.current?.focus?.();
    };
  }, [active, containerRef, onEscape, initialFocusRef]);
}
