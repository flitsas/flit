'use client';

import { PenLine } from 'lucide-react';

/** Destino del botón «Editar»: el nombre del paso (para el texto accesible) y cómo llegar a él. */
export interface DestinoEdicion {
  pasoLabel: string;
  ir: () => void;
}

/**
 * Botón «Editar» de la cabecera de una tarjeta del Resumen: lleva al paso del asistente donde se
 * captura esa sección. El texto visible es corto; el nombre accesible dice a qué paso lleva, para
 * que el lector de pantalla no oiga varios «Editar» iguales. Vive en su propio archivo porque lo usan
 * `MatriculaResumen` y `ExpedienteVisor`, y este último ya es importado por el primero.
 */
export function EditarSeccionButton({
  title,
  destino,
}: {
  title: string;
  destino: DestinoEdicion;
}) {
  return (
    <button
      type="button"
      onClick={destino.ir}
      aria-label={`Editar ${title}: ir al paso «${destino.pasoLabel}»`}
      title={`Ir al paso «${destino.pasoLabel}» para corregir estos datos`}
      className="ml-auto inline-flex h-8 shrink-0 items-center gap-1.5 rounded-xl border px-3 text-xs font-semibold transition hover:opacity-80 focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-[var(--color-flit-brand)]"
      style={{ borderColor: '#DFE5ED', color: 'var(--color-flit-brand)' }}
    >
      <PenLine className="h-3.5 w-3.5" aria-hidden="true" />
      Editar
    </button>
  );
}
