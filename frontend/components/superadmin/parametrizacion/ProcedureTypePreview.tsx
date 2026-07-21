'use client';

import { useEffect, useState } from 'react';
import { superadminClient } from '@/lib/api/superadmin-client';
import type { ProcedureStep } from '@/lib/api/types/procedure-parametrization';

interface ProcedureTypePreviewProps {
  typeId: string;
}

type PreviewState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; steps: ProcedureStep[] };

/**
 * Vista de SOLO LECTURA de los pasos configurados de un tipo de trámite (botón "Visualizar"
 * del Configurador). Reutiliza la misma representación de pasos que el wizard/los trámites
 * actuales: círculo numerado (#557EFF) + título del paso, con sus secciones debajo. No permite
 * editar ni reordenar — solo revisar el flujo tal como lo verá el operario.
 */
export function ProcedureTypePreview({ typeId }: ProcedureTypePreviewProps) {
  const [state, setState] = useState<PreviewState>({ kind: 'loading' });

  useEffect(() => {
    let active = true;
    // Reset a "cargando" al (re)montar o cambiar de tipo; el skeleton es intencional. El estado
    // inicial ya es 'loading', así que en el primer render no hay cambio visible.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setState({ kind: 'loading' });
    superadminClient
      .getSteps(typeId)
      .then((steps) => {
        if (!active) return;
        setState({
          kind: 'loaded',
          steps: [...steps].sort((a, b) => a.sortOrder - b.sortOrder),
        });
      })
      .catch(() => {
        if (!active) return;
        setState({ kind: 'error', message: 'No se pudo cargar la configuración del trámite.' });
      });
    return () => {
      active = false;
    };
  }, [typeId]);

  if (state.kind === 'loading') {
    return (
      <p className="text-xs opacity-60" role="status" aria-live="polite">
        Cargando pasos…
      </p>
    );
  }

  if (state.kind === 'error') {
    return (
      <div
        className="rounded-xl p-3 text-xs border"
        style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
        role="alert"
        aria-live="polite"
      >
        {state.message}
      </div>
    );
  }

  if (state.steps.length === 0) {
    return (
      <p className="text-xs opacity-60" role="status">
        Este trámite aún no tiene pasos configurados.
      </p>
    );
  }

  return (
    <ol className="space-y-2" aria-label="Pasos configurados del trámite">
      {state.steps.map((step, index) => (
        <li
          key={step.id ?? step.code}
          className="flex items-start gap-3 rounded-xl p-3 border bg-[rgba(85,126,255,0.03)] dark:bg-white/5"
        >
          <span
            className="h-7 w-7 rounded-full grid place-items-center text-[11px] font-bold text-white shrink-0"
            style={{ background: '#557EFF' }}
            aria-hidden="true"
          >
            {index + 1}
          </span>
          <div className="min-w-0 flex-1">
            <p className="text-xs font-semibold">{step.title}</p>
            {step.sections.length > 0 && (
              <ul className="mt-1 space-y-0.5">
                {step.sections
                  .slice()
                  .sort((a, b) => a.sortOrder - b.sortOrder)
                  .map((section) => (
                    <li key={section.id ?? section.code} className="text-[11px] opacity-60">
                      • {section.title}
                      {section.formFields.length > 0 && (
                        <span className="opacity-70">
                          {' '}
                          ({section.formFields.length}{' '}
                          {section.formFields.length === 1 ? 'campo' : 'campos'})
                        </span>
                      )}
                    </li>
                  ))}
              </ul>
            )}
          </div>
        </li>
      ))}
    </ol>
  );
}
