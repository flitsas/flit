'use client';

import { useMemo } from 'react';
import { Icono } from "@flit/ui/Icono";
import type { WizardStep, WizardStepStatus } from '@/lib/api/types/procedure-runtime';
import { reasonCopy, reasonsSummary, stepLabelCopy } from './wizard-copy';
import { canNavigateToStep } from './wizard-navigation';
import {
  coalesceActorSteps,
  displayIndexForActive,
  sourceIndexForDisplayClick,
  type DisplayWizardStep,
} from './wizard-actores-coalesce';

/**
 * Icono/marcador por status del paso (✓ / número activo / outline / 🔒).
 *
 * Exportado para que el detalle del trámite dibuje su stepper con ESTE marcador y no con una
 * segunda versión: los círculos del wizard y los del detalle son el mismo elemento del sistema.
 */
export function StepMarker({
  status,
  index,
  active = false,
  compacto = false,
}: {
  status: WizardStepStatus;
  index: number;
  active?: boolean;
  /** Modo condensado: el mismo marcador, un punto más pequeño. */
  compacto?: boolean;
}) {
  // Marcador `.paso__num` de la propuesta (styles/tramites-propuesta.css): hecho en turquesa con
  // check, el paso en curso relleno en azul de acción con halo, lo pendiente en cielo con aro gris
  // y lo bloqueado con aro punteado y candado.
  const base = `di-paso__num ${compacto ? 'h-6 w-6' : 'h-8 w-8'}`;
  if (status === 'complete' && !active) {
    return (
      <span className={`${base} di-paso__num--hecho`} aria-hidden="true">
        <Icono nombre="check" className={compacto ? 'h-3 w-3' : 'h-4 w-4'} />
      </span>
    );
  }
  if (status === 'locked' && !active) {
    return (
      <span className={`${base} di-paso__num--bloqueado`} aria-hidden="true">
        <Icono nombre="candado" className={compacto ? 'h-3 w-3' : 'h-3.5 w-3.5'} />
      </span>
    );
  }
  return (
    <span className={`${base} ${active ? 'di-paso__num--actual' : ''}`} aria-hidden="true">
      {index + 1}
    </span>
  );
}

export type WizardStepTrackerProps = {
  steps: WizardStep[];
  activeIndex: number;
  onGoToStep: (index: number) => void;
  /** Solo lectura / vista: misma cascada de navegación que el wizard. */
  viewOnly?: boolean;
  /**
   * Traspaso: fusiona vendedor+comprador en un solo ítem visual "Actores".
   * Matrícula no aplica (queda 1:1).
   */
  coalesceActores?: boolean;
  /**
   * Modo condensado, para cuando el asistente ya está en marcha y el seguimiento compite por alto
   * con el formulario. Mismo diseño —círculos, conector y colores por estado no cambian—, solo
   * más apretado: marcadores de 24px en vez de 32, y el rótulo visible solo en el paso en curso.
   *
   * Los rótulos de los demás pasos NO se eliminan: siguen en el nombre accesible del botón
   * (`Paso N: Nombre (estado)`), así que quien navega por teclado o lector de pantalla los sigue
   * oyendo completos. Y los motivos del paso incompleto tampoco se pierden: el pie del asistente y
   * el propio formulario dicen qué falta; aquí solo dejan de ocupar una línea que además cambiaba
   * de alto al pasar de un paso a otro, haciendo saltar el contenido.
   */
  compacto?: boolean;
};

/**
 * Asistente de seguimiento horizontal con el avance del UI Kit 2026 (`.kit-pasos`).
 * Va dentro del chrome sticky del wizard (título + pasos). Verde = completo, azul = paso activo.
 */
export function WizardStepTracker({
  steps,
  activeIndex,
  onGoToStep,
  viewOnly = false,
  coalesceActores = false,
  compacto = false,
}: WizardStepTrackerProps) {
  const displaySteps: DisplayWizardStep[] = useMemo(
    () => (coalesceActores ? coalesceActorSteps(steps) : steps.map((s, i) => ({ ...s, sourceIndexes: [i] }))),
    [steps, coalesceActores],
  );
  const displayActive = displayIndexForActive(displaySteps, activeIndex);

  if (displaySteps.length === 0) return null;

  return (
    // UI Kit 2026: avance horizontal del asistente (`.kit-pasos` / `.kit-paso`, como PasosHorizontal) a cualquier
    // ancho, dentro de la tarjeta de la propuesta (`nav.card`). La línea entre pasos la dibuja
    // `.kit-paso + .kit-paso::before` según `data-estado`: azul hasta el paso en curso (recorrido, no completitud).
    <nav
      aria-label="Asistente de seguimiento"
      className="w-full rounded-[9px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)]"
    >
      <ol className={`kit-pasos w-full min-w-0 items-start ${compacto ? 'pt-2.5' : ''}`}>
        {displaySteps.map((s, i) => {
          const isActive = i === displayActive;
          const clickable = s.sourceIndexes.some((idx) => canNavigateToStep(steps, idx, viewOnly));
          // Las líneas marcan RECORRIDO, no completitud: van en azul hasta el paso en curso. Coloreadas por
          // `status === 'complete'` un paso saltado dejaba el hilo gris en medio del recorrido.
          const estadoVisual = isActive ? 'actual' : i < displayActive ? 'hecho' : 'pendiente';
          // Nombre en la nomenclatura del diseño; cae al del servidor si la clave no está mapeada.
          const label = stepLabelCopy(s.key, s.label);
          return (
            <li
              key={s.key}
              // Condensado: círculo de 24 px, así que la línea baja a su centro (12 px).
              className={`kit-paso min-w-0 ${compacto ? 'before:!top-[11px]' : ''}`}
              data-estado={estadoVisual}
              aria-current={isActive ? 'step' : undefined}
            >
              <button
                type="button"
                onClick={() => onGoToStep(sourceIndexForDisplayClick(s))}
                disabled={!clickable}
                // El foco estaba roto: la clase venía escrita como
                // `focus-visible:ring-flit-brand/focus-visible:ring-offset-2` (sin separar), que
                // Tailwind no reconoce — el paso activo no mostraba anillo alguno al tabular.
                className={`kit-paso__boton relative z-10 rounded-[9px] px-1 outline-none disabled:cursor-not-allowed focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)] ${compacto ? 'gap-1' : 'gap-1.5'}`}
                aria-label={`Paso ${i + 1}: ${label} (${s.status})`}
              >
                <StepMarker status={s.status} index={i} active={isActive} compacto={compacto} />
                <span className="min-w-0 w-full">
                  {/* Condensado: solo se ve el rótulo del paso en curso. Los demás pasan a `sr-only`
                      —no se borran— así que el lector de pantalla los sigue leyendo y el nombre
                      accesible del botón los conserva enteros. Los círculos siguen contando la
                      historia a la vista: cuántos pasos hay, cuáles van y en cuál estás. */}
                  <span
                    className={`kit-paso__etiqueta block truncate leading-snug ${
                      compacto && !isActive ? 'sr-only' : ''
                    }`}
                    title={label}
                  >
                    {label}
                  </span>
                  {/* Los motivos desaparecen al condensar. No solo por alto: son la única parte del
                      seguimiento cuyo tamaño CAMBIA según el paso, y con la cabecera fija eso hacía
                      saltar el formulario entero al navegar. Qué falta se sigue diciendo en el pie
                      y en el propio paso. */}
                  {!compacto && isActive && s.status === 'incomplete' && s.reasons.length > 0 && (
                    <span
                      className="mt-1 block truncate text-xs"
                      style={{ color: 'var(--di-text-tertiary)' }}
                      // El detalle completo queda al alcance sin ocupar la línea de tiempo.
                      title={s.reasons.map(reasonCopy).join(' · ')}
                    >
                      {reasonsSummary(s.reasons)}
                    </span>
                  )}
                </span>
              </button>
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
