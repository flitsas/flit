"use client";

import { useEffect, useId, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { OT_OVERLAY_STYLE, OT_SHEET_CLASS } from "./ot-detalle-visual";

/**
 * Armazón del modal de detalle del trámite del ORGANISMO DE TRÁNSITO (HU #12060).
 *
 * Copia local de `DetalleTramiteShell` con una diferencia de fondo: el prototipo del OT reparte la
 * hoja en tres franjas —encabezado fijo, cuerpo desplazable, pie fijo— en vez de desplazar la hoja
 * entera. Eso no es cosmético: el pie lleva Aprobar y Rechazar, y con el acordeón de documentos
 * abierto quedarían fuera de pantalla si el modal se desplazara completo.
 *
 * Se duplica en vez de compartirse por lo mismo que `ot-detalle-visual.ts`: el detalle del gestor
 * NO debe moverse cuando se mueva el del OT.
 */
export function OtDetalleShell({
  open,
  onClose,
  title,
  header,
  footer,
  children,
  busy = false,
}: {
  open: boolean;
  onClose: () => void;
  /** Nombre accesible del diálogo cuando `header` no pinta un encabezado propio. */
  title: string;
  header?: (ctx: { titleId: string }) => ReactNode;
  /** Pie fijo con las acciones de decisión. Sin él, la franja no se pinta. */
  footer?: ReactNode;
  children: ReactNode;
  /** Con una acción en curso el modal no se cierra: cerrarlo dejaría la acción sin destino. */
  busy?: boolean;
}) {
  const titleId = useId();

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !busy) onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, busy, onClose]);

  if (!open || typeof document === "undefined") return null;

  const requestClose = () => {
    if (!busy) onClose();
  };

  return createPortal(
    <div
      className="fixed inset-0 z-[1100] flex items-center justify-center p-4"
      style={OT_OVERLAY_STYLE}
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      onMouseDown={(e) => {
        // `mousedown` y no `click`: con `click`, arrastrar el cursor desde dentro del modal hasta
        // el fondo (al seleccionar texto) lo cerraría.
        if (e.target === e.currentTarget) requestClose();
      }}
    >
      <div className={OT_SHEET_CLASS} onMouseDown={(e) => e.stopPropagation()}>
        <div className="shrink-0">
          {header ? (
            header({ titleId })
          ) : (
            <h2 id={titleId} className="sr-only">
              {title}
            </h2>
          )}
        </div>

        <div className="mt-3 min-h-0 flex-1 space-y-3 overflow-y-auto scroll-smooth pr-2">
          {children}
        </div>

        {footer ? <div className="mt-4 shrink-0">{footer}</div> : null}
      </div>
    </div>,
    document.body,
  );
}
