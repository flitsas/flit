'use client';

import { INTERRUPTOR_CLS, INTERRUPTOR_PERILLA_CLS } from '@flit/ui/Interruptor';
import { useState } from 'react';
import { Icono } from "@flit/ui/Icono";
import { SuperadminApiError, superadminClient } from '@/lib/api/superadmin-client';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

/**
 * Barrera de operación (ADR-0050): si el gestor puede elegir este tipo al crear un trámite.
 *
 * Es la palanca que convierte «habilitar un trámite» en configuración y no en un despliegue. Va
 * separada de la publicación a propósito: publicar dice que el tipo EXISTE en el catálogo;
 * habilitar, que su recorrido ya se puede recorrer.
 *
 * Encender exige que el tipo esté listo. Cuando no lo está, el backend responde con la lista de lo
 * que falta y se pinta entera: enterarse de un impedimento por vez convierte dar de alta un tipo en
 * una sucesión de intentos.
 */
export function TipoTramiteBarrera({
  tipo,
  onCambiado,
}: {
  tipo: ProcedureTypeSummary;
  onCambiado: (actualizado: ProcedureTypeSummary) => void;
}) {
  const [guardando, setGuardando] = useState(false);
  const [impedimentos, setImpedimentos] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);

  const alternar = async () => {
    setGuardando(true);
    setImpedimentos([]);
    setError(null);
    try {
      const actualizado = await superadminClient.setWizardEnabled(tipo.id, !tipo.wizardEnabled);
      onCambiado(actualizado);
    } catch (e: unknown) {
      const motivos = extraerMotivos(e);
      if (motivos.length > 0) setImpedimentos(motivos);
      else setError(e instanceof Error ? e.message : 'No se pudo cambiar la barrera.');
    } finally {
      setGuardando(false);
    }
  };

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-3">
        <button
          type="button"
          role="switch"
          aria-checked={tipo.wizardEnabled}
          aria-label={`Operable en el asistente: ${tipo.name}`}
          disabled={guardando}
          onClick={() => void alternar()}
          className={INTERRUPTOR_CLS}
        >
          <span className={INTERRUPTOR_PERILLA_CLS} />
        </button>
        <span className="text-xs font-medium text-flit-primary">
          {guardando ? (
            <span className="inline-flex items-center gap-1.5">
              <Icono nombre="girador" className="h-3 w-3 animate-spin" />
              Guardando…
            </span>
          ) : tipo.wizardEnabled ? (
            'Operable: el gestor puede elegirlo'
          ) : (
            'No operable: no aparece en el selector'
          )}
        </span>
      </div>

      {impedimentos.length > 0 && (
        <div
          className="rounded-xl border px-3 py-2.5 text-xs"
          style={{ borderColor: 'var(--badge-warning-border)', background: 'var(--badge-warning-bg)' }}
          role="alert"
        >
          <p className="mb-1.5 flex items-center gap-1.5 font-semibold" style={{ color: 'var(--badge-warning-fg)' }}>
            <Icono nombre="alerta" className="h-3.5 w-3.5" />
            Todavía no se puede habilitar
          </p>
          <ul className="ml-4 list-disc space-y-1 text-flit-primary/80 dark:text-muted-foreground">
            {impedimentos.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        </div>
      )}

      {error && (
        <p className="text-xs" role="alert" style={{ color: 'var(--destructive)' }}>
          {error}
        </p>
      )}
    </div>
  );
}

/**
 * El 422 de la barrera trae `{ motivos: [...] }`. Si la forma no coincide se devuelve vacío y el
 * llamador cae al mensaje genérico, en vez de romper la pantalla por un contrato inesperado.
 */
function extraerMotivos(e: unknown): string[] {
  if (!(e instanceof SuperadminApiError)) return [];
  const motivos = (e.body as { motivos?: unknown })?.motivos;
  return Array.isArray(motivos) ? motivos.filter((m): m is string => typeof m === 'string') : [];
}
