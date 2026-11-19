"use client";

import { useId } from "react";
import { INTERRUPTOR_CLS, INTERRUPTOR_PERILLA_CLS } from "@flit/ui/Interruptor";

/**
 * Interruptor accesible (role=switch) con su etiqueta y una ayuda opcional. Todo va dentro de un <label>: un clic en el
 * texto activa el interruptor, y la ayuda queda enlazada con aria-describedby.
 *
 * `soloControl`: para los que GUARDAN AL INSTANTE (sin botón Guardar). El texto no se envuelve en el <label>, así que
 * solo el interruptor cambia el valor y un clic en el título o la ayuda no dispara nada.
 */
export function Interruptor({
  activo,
  onCambio,
  etiqueta,
  ayuda,
  deshabilitado,
  soloControl = false,
}: {
  activo: boolean;
  onCambio: (v: boolean) => void;
  etiqueta: string;
  ayuda?: string;
  deshabilitado?: boolean;
  soloControl?: boolean;
}) {
  const idAyuda = useId();
  const Contenedor = soloControl ? "div" : "label";
  return (
    <Contenedor className={`flex items-start gap-3 ${deshabilitado ? "cursor-default opacity-60" : soloControl ? "" : "cursor-pointer"}`}>
      <button
        type="button"
        role="switch"
        aria-checked={activo}
        aria-label={etiqueta}
        aria-describedby={ayuda ? idAyuda : undefined}
        disabled={deshabilitado}
        onClick={() => onCambio(!activo)}
        className={`${INTERRUPTOR_CLS} mt-0.5`}
      >
        <span className={INTERRUPTOR_PERILLA_CLS} />
      </button>
      <span className="flex flex-col">
        <span className="text-xs font-semibold text-flit-primary">{etiqueta}</span>
        {ayuda && (
          <span id={idAyuda} className="text-xs text-muted-foreground">
            {ayuda}
          </span>
        )}
      </span>
    </Contenedor>
  );
}
