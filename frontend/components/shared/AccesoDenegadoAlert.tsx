"use client";

import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { PORTAL_PN_MODULE_ID } from "@/lib/nav/module-access";

/** Texto único del aviso: lo muestran tanto `/?m=` sin permiso como la redirección del middleware (ADR-0074 §4). */
export const ACCESO_DENEGADO_MENSAJE = "No tienes acceso a este módulo.";

/** Módulos con un motivo más preciso que el texto único. */
const MENSAJE_POR_MODULO: Record<string, string> = {
  [PORTAL_PN_MODULE_ID]: "Solo el SuperAdmin o el administrador del organismo pueden configurar el portal Persona Natural.",
};

/**
 * AlertCard de acceso denegado (HU #13650, Feature #13102). Aparece en el inicio cuando se intentó entrar por URL a un
 * módulo sin permiso efectivo; el módulo nunca se monta. Tono de aviso (ámbar FLIT, `InlineAlert`), anunciado como
 * `alert` para lectores de pantalla, con botón de cierre alcanzable por teclado.
 */
export function AccesoDenegadoAlert({ onClose, modulo }: { onClose: () => void; modulo?: string | null }) {
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
          className="rounded-md p-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
          style={{ color: "var(--badge-warning-fg)" }}
        >
          <Icono nombre="cerrar" className="h-3.5 w-3.5" />
        </button>
      }
    >
      {(modulo && MENSAJE_POR_MODULO[modulo]) || ACCESO_DENEGADO_MENSAJE}
    </InlineAlert>
  );
}
