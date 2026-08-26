'use client';

import { useEffect, useId, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { DETALLE_OVERLAY_STYLE, DETALLE_SHEET_CLASS } from './detalle-visual';

/**
 * Shell del modal Detalle — NO es el átomo Modal blanco (anti-patrón spec flit-detalle-tramite).
 * Canvas #EEF5FF directamente sobre overlay navy blur(6px).
 */
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

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !busy) onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open, busy, onClose]);

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
