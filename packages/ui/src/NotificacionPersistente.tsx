"use client";

import type { ReactNode } from "react";
import { cn } from "./cn";
import { useRegionVivaLista } from "./useRegionViva";

/**
 * Aviso persistente (HU #14562 · UI-1): `role="status"`, sin autocierre; solo se va con «Cerrar» (`onCerrar`).
 * La posición (arriba a la derecha) la decide quien lo monta, por `className`.
 * HU #14562 · M11: se monta como región `aria-live` vacía y recibe el contenido (y el `role="status"`) un instante
 * después, para que el lector de pantalla lo anuncie: una región que nace con su texto no se anuncia de forma fiable.
 */
export interface AccionNotificacion {
  label: string;
  onClick?: () => void;
  href?: string;
}

export interface NotificacionPersistenteProps {
  titulo: string;
  mensaje?: ReactNode;
  accion?: AccionNotificacion;
  onCerrar: () => void;
  etiquetaCerrar?: string;
  className?: string;
}

const CLASE_ACCION =
  "inline-flex min-h-9 items-center rounded-full bg-[image:var(--di-gradient-action)] px-4 text-sm font-semibold text-[color:var(--di-on-action)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]";

export function NotificacionPersistente({
  titulo,
  mensaje,
  accion,
  onCerrar,
  etiquetaCerrar = "Cerrar notificación",
  className,
}: NotificacionPersistenteProps) {
  const lista = useRegionVivaLista();
  if (!lista) return <div aria-live="polite" aria-atomic="true" />;
  return (
    <div
      role="status"
      aria-live="polite"
      aria-atomic="true"
      className={cn(
        "flex w-full max-w-sm items-start gap-3 rounded-[var(--di-radius-modal)] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-4 shadow-[var(--kit-sombra)] motion-safe:transition-opacity motion-safe:duration-[var(--di-motion-base)]",
        className,
      )}
    >
      <div className="flex min-w-0 flex-1 flex-col gap-2">
        <p className="text-sm font-semibold text-[color:var(--di-text-primary)]">{titulo}</p>
        {mensaje ? <div className="text-sm text-[color:var(--di-text-secondary)]">{mensaje}</div> : null}
        {accion ? (
          <div>
            {accion.href ? (
              <a href={accion.href} onClick={accion.onClick} className={CLASE_ACCION}>
                {accion.label}
              </a>
            ) : (
              <button type="button" onClick={accion.onClick} className={CLASE_ACCION}>
                {accion.label}
              </button>
            )}
          </div>
        ) : null}
      </div>
      <button
        type="button"
        aria-label={etiquetaCerrar}
        onClick={onCerrar}
        className="inline-flex size-9 items-center justify-center rounded-full text-[color:var(--di-text-secondary)] hover:bg-[color:var(--kit-hover)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)]"
      >
        <span aria-hidden="true">×</span>
      </button>
    </div>
  );
}
