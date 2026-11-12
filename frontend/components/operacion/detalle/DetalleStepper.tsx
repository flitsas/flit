'use client';

import type { ComponentType } from "react";
import { Icono } from "@flit/ui/Icono";
import { DETALLE_CARD } from './detalle-visual';

export interface DetallePaso {
  id: string;
  label: string;
  Icon: ComponentType<{ className?: string }>;
  /** Paso cumplido (círculo turquesa + check si no está seleccionado). */
  completo: boolean;
}

/**
 * Stepper del detalle con el avance horizontal del UI Kit 2026 (`.kit-pasos`), con ícono/check en el círculo.
 * Distinto del StepMarker del wizard (28px + halo).
 */
export function DetalleStepper({
  pasos,
  pasoActivoId,
  onSelect,
  integrado = false,
}: {
  pasos: DetallePaso[];
  pasoActivoId: string;
  onSelect: (id: string) => void;
  /** HU #14482 — dentro de la tarjeta de resumen: sin tarjeta propia. */
  integrado?: boolean;
}) {
  return (
    <div className={integrado ? 'overflow-x-auto' : `${DETALLE_CARD} mt-3 overflow-x-auto p-4`}>
      {/* UI Kit 2026: avance horizontal del kit (`.kit-pasos`): círculos de 32 px unidos por la línea de 2 px que
          dibuja `.kit-paso + .kit-paso::before`; hecho en verde, el abierto con borde azul y aro, lo que falta en gris.
          Roles, ids y manejadores de las pestañas quedan iguales. */}
      <div className="kit-pasos min-w-[760px] items-start" role="tablist" aria-label="Pasos del trámite">
        {pasos.map((paso, i) => {
          const isSel = paso.id === pasoActivoId;
          const done = paso.completo && !isSel;
          const estado = isSel ? 'actual' : done ? 'hecho' : 'pendiente';

          return (
            <div key={paso.id} className="kit-paso" data-estado={estado}>
              <button
                type="button"
                role="tab"
                id={`detalle-tab-${paso.id}`}
                aria-selected={isSel}
                aria-controls={`detalle-panel-${paso.id}`}
                aria-label={`Paso ${i + 1}: ${paso.label}`}
                onClick={() => onSelect(paso.id)}
                className="kit-paso__boton rounded-[9px] py-1 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)]"
              >
                <span className="kit-paso__num" aria-hidden="true">
                  {done ? (
                    // Check del kit de 18 px, como `PasosHorizontal`: trazo en currentColor, blanco sobre el disco verde.
                    <Icono nombre="check" className="h-[18px] w-[18px]" />
                  ) : (
                    <paso.Icon className="h-4 w-4" />
                  )}
                </span>
                <span className="kit-paso__etiqueta whitespace-nowrap">
                  {i + 1}. {paso.label}
                </span>
              </button>
            </div>
          );
        })}
      </div>
    </div>
  );
}
