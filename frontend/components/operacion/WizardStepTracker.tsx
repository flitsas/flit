'use client';

import { useMemo } from 'react';
import { Check, Lock } from 'lucide-react';
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
        <Check className={compacto ? 'h-3 w-3' : 'h-4 w-4'} strokeWidth={3} />
      </span>
    );
  }
  if (status === 'locked' && !active) {
    return (
      <span className={`${base} di-paso__num--bloqueado`} aria-hidden="true">
        <Lock className={compacto ? 'h-3 w-3' : 'h-3.5 w-3.5'} />
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
 * Asistente de seguimiento horizontal (patrón TimelineProcess / wizard FLIT).
 * Va dentro del chrome sticky del wizard (título + pasos). Etiquetas siempre
 * visibles; verde = completo, azul = paso activo. Sin card blanca contenedora.
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
    // Desde lg es la columna de pasos de la propuesta (`.asistente > nav.card` + `.pasos`): tarjeta
    // con la lista en vertical, marcador a la izquierda y rótulo siempre visible. Debajo de lg sigue
    // siendo el seguimiento horizontal.
    <nav
      aria-label="Asistente de seguimiento"
      className={`${compacto ? 'w-full' : 'w-full py-1'} lg:rounded-[20px] lg:border lg:border-[var(--di-border)] lg:bg-[var(--di-surface-elevated)] lg:p-4 lg:shadow-[var(--shadow-card)]`}
    >
      <ol className="flex w-full min-w-0 items-start gap-0 lg:flex-col lg:items-stretch lg:gap-1">
        {displaySteps.map((s, i) => {
          const isActive = i === displayActive;
          const clickable = s.sourceIndexes.some((idx) => canNavigateToStep(steps, idx, viewOnly));
          // Las líneas marcan RECORRIDO, no completitud: en la propuesta van en azul hasta el paso
          // en curso. Coloreadas por `status === 'complete'` un paso saltado dejaba el hilo gris en
          // medio del recorrido y parecía que el asistente se había roto.
          const prevComplete = i <= displayActive;
          const lineAfterGreen = i < displayActive;
          // Nombre en la nomenclatura del diseño; cae al del servidor si la clave no está mapeada.
          const label = stepLabelCopy(s.key, s.label);
          return (
            <li
              key={s.key}
              className="relative flex min-w-0 flex-1 flex-col items-center lg:flex-none lg:items-stretch"
              aria-current={isActive ? 'step' : undefined}
            >
              {i > 0 && (
                <span
                  className={`pointer-events-none absolute left-0 right-1/2 h-[3px] -translate-y-1/2 lg:hidden ${compacto ? 'top-3' : 'top-4'}`}
                  style={{ background: prevComplete ? 'var(--color-flit-tech)' : 'var(--di-surface-sunken)' }}
                  aria-hidden="true"
                />
              )}
              {i < displaySteps.length - 1 && (
                <span
                  className={`pointer-events-none absolute left-1/2 right-0 h-[3px] -translate-y-1/2 lg:hidden ${compacto ? 'top-3' : 'top-4'}`}
                  style={{ background: lineAfterGreen ? 'var(--color-flit-tech)' : 'var(--di-surface-sunken)' }}
                  aria-hidden="true"
                />
              )}
              <button
                type="button"
                onClick={() => onGoToStep(sourceIndexForDisplayClick(s))}
                disabled={!clickable}
                // El foco estaba roto: la clase venía escrita como
                // `focus-visible:ring-flit-brand/focus-visible:ring-offset-2` (sin separar), que
                // Tailwind no reconoce — el paso activo no mostraba anillo alguno al tabular.
                className={`relative z-10 flex w-full flex-col items-center rounded-lg px-1 text-center outline-none disabled:cursor-not-allowed focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 lg:min-h-[52px] lg:flex-row lg:gap-3 lg:rounded-xl lg:px-2.5 lg:py-2 lg:text-left lg:enabled:hover:bg-[var(--di-accent-soft)] ${isActive ? 'lg:bg-[var(--di-accent-soft)]' : ''} ${compacto ? 'gap-1' : 'gap-2'}`}
                aria-label={`Paso ${i + 1}: ${label} (${s.status})`}
              >
                <StepMarker status={s.status} index={i} active={isActive} compacto={compacto} />
                <span className="min-w-0 w-full">
                  {/* Condensado: solo se ve el rótulo del paso en curso. Los demás pasan a `sr-only`
                      —no se borran— así que el lector de pantalla los sigue leyendo y el nombre
                      accesible del botón los conserva enteros. Los círculos siguen contando la
                      historia a la vista: cuántos pasos hay, cuáles van y en cuál estás. */}
                  <span
                    className={`block truncate text-xs leading-snug lg:whitespace-normal lg:text-sm ${
                      isActive ? 'font-bold' : 'font-medium lg:font-semibold'
                    } ${compacto && !isActive ? 'sr-only lg:not-sr-only' : ''}`}
                    style={
                      isActive
                        ? { color: 'var(--di-action)' }
                        : s.status === 'complete'
                          ? { color: 'var(--di-heading)' }
                          : { color: 'var(--di-text-secondary)' }
                    }
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
                      className="mt-1 block truncate text-xs lg:whitespace-normal"
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
