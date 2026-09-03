"use client";

import { useId, type ReactNode } from "react";
import { ChevronDown } from "lucide-react";
import { OT_BLUE, OT_CARD } from "./ot-detalle-visual";

/**
 * Acordeón del detalle del OT (HU #12060) — el bloque que sustituye a la navegación por pasos.
 *
 * El cambio no es solo de forma: con el stepper solo existía en el DOM la sección activa, y para
 * cotejar los documentos con los datos del vehículo había que ir y volver. Los tres acordeones son
 * independientes, así que el revisor puede tener abiertos a la vez los que necesita comparar.
 *
 * Cada uno abre y cierra por su cuenta —no hay «uno abierto a la vez»— porque esa es justamente la
 * razón de ser del cambio.
 */
export function OtDetalleAcordeon({
  titulo,
  abierto,
  onToggle,
  derecha,
  children,
}: {
  titulo: string;
  abierto: boolean;
  onToggle: () => void;
  /** Contenido a la derecha del encabezado (un contador, un sello). Fuera del botón: no lo activa. */
  derecha?: ReactNode;
  children: ReactNode;
}) {
  const panelId = useId();

  return (
    <div className={OT_CARD}>
      <div className="flex items-center gap-3 px-4 py-3">
        <button
          type="button"
          onClick={onToggle}
          aria-expanded={abierto}
          aria-controls={panelId}
          className="flex min-w-0 flex-1 items-center justify-between gap-3 rounded-lg text-left focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
        >
          <span className="text-sm font-semibold" style={{ color: OT_BLUE }}>
            {titulo}
          </span>
          <ChevronDown
            className={`h-4 w-4 shrink-0 transition-transform ${abierto ? "rotate-180" : ""}`}
            style={{ color: OT_BLUE }}
            aria-hidden="true"
          />
        </button>
        {derecha ? <div className="shrink-0">{derecha}</div> : null}
      </div>

      {/* El panel se desmonta al cerrar, como en el prototipo: las secciones del detalle del OT
          piden datos al montar, y mantenerlas montadas en un acordeón cerrado las haría cargar
          expediente y adjuntos que nadie está mirando. */}
      {abierto ? (
        <div id={panelId} role="region" aria-label={titulo} className="px-4 pb-4 pt-1">
          {children}
        </div>
      ) : (
        <div id={panelId} hidden />
      )}
    </div>
  );
}
