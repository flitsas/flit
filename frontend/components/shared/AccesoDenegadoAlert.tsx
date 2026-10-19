"use client";

import { X } from "lucide-react";
import { InlineAlert } from "@/components/atom/InlineAlert";

/** Texto único del aviso: lo muestran tanto `/?m=` sin permiso como la redirección del middleware (ADR-0072 §4). */
export const ACCESO_DENEGADO_MENSAJE = "No tienes acceso a este módulo.";

/**
 * AlertCard de acceso denegado (HU #13650, Feature #13102). Aparece en el inicio cuando se intentó entrar por URL a un
 * módulo sin permiso efectivo; el módulo nunca se monta. Tono de aviso (ámbar FLIT, `InlineAlert`), anunciado como
 * `alert` para lectores de pantalla, con botón de cierre alcanzable por teclado.
 */
export function AccesoDenegadoAlert({ onClose }: { onClose: () => void }) {
  return (
    <InlineAlert
      tone="warning"
      className="mx-6 mt-4"
      id="aviso-acceso-denegado"
      action={
        <button
          type="button"
          onClick={onClose}
          aria-label="Cerrar aviso de acceso denegado"
          className="rounded-md p-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          style={{ color: "var(--badge-warning-fg)" }}
        >
          <X className="h-3.5 w-3.5" aria-hidden="true" />
        </button>
      }
    >
      {ACCESO_DENEGADO_MENSAJE}
    </InlineAlert>
  );
}
