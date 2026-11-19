"use client";

import { iconoComo } from "@flit/ui/iconoComo";
import { Tooltip } from "@flit/ui/Tooltip";

/**
 * Texto fijo del tooltip (contrato 1.6.0 §1.2: literal del front, no viaja). SP-19: es lo único que dice el ícono; nunca el
 * documento declarado ni el del RUNT, ni enmascarados.
 */
export const TEXTO_CAMBIO_PROPIETARIO =
  "Hubo un cambio de propietario: el documento declarado no coincide con el propietario actual reportado por el RUNT.";

const IconoIntercambio = iconoComo("intercambio");

/** Toggletip de 24 × 24 (T6), redondo, con foco visible del kit y hover --kit-hover. */
const BOTON_CLS =
  "inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-[color:var(--di-warning)] hover:bg-[var(--kit-hover)] " +
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]";

/**
 * Ícono de cambio de propietario (HU #14585, spec-ui §6.5, A6.7). Solo con `cambio === true` (el backend comparó el documento
 * declarado contra los propietarios del RUNT); `false` y `null` no pintan nada. Va a la derecha de la placa en la grilla del
 * lote (compañía y backoffice), en la tabla de Vehículos y en el panel del dictamen. El clic no hace nada más que alternar el
 * tooltip en táctil. Por debajo de 900 px suma el texto «Cambio de propietario» (aria-hidden: el botón ya lo nombra) para no
 * depender del tooltip.
 */
export function IconoCambioPropietario({ cambio }: { cambio?: boolean | null }) {
  if (cambio !== true) return null;
  return (
    <span className="inline-flex items-center gap-1">
      <Tooltip contenido={TEXTO_CAMBIO_PROPIETARIO}>
        <button type="button" aria-label="Cambio de propietario" className={BOTON_CLS}>
          <IconoIntercambio className="h-4 w-4" aria-hidden="true" />
        </button>
      </Tooltip>
      <span
        data-cambio-propietario-movil
        aria-hidden="true"
        className="hidden text-xs font-semibold text-[color:var(--di-warning)] max-[900px]:inline"
      >
        Cambio de propietario
      </span>
    </span>
  );
}
