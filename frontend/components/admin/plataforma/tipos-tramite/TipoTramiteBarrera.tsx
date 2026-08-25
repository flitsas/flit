'use client';

import { useState } from 'react';
import { AlertTriangle, Loader2 } from 'lucide-react';
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
          className="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:opacity-50"
          style={{ background: tipo.wizardEnabled ? '#557EFF' : '#C8D2E0' }}
        >
          <span
            className="inline-block h-4 w-4 transform rounded-full bg-white transition"
            style={{ transform: tipo.wizardEnabled ? 'translateX(1.5rem)' : 'translateX(0.25rem)' }}
          />
        </button>
        <span className="text-xs font-medium text-[#162744] dark:text-white">
          {guardando ? (
            <span className="inline-flex items-center gap-1.5">
              <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
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
          style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)' }}
          role="alert"
        >
          <p className="mb-1.5 flex items-center gap-1.5 font-semibold" style={{ color: '#B87A00' }}>
            <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" />
            Todavía no se puede habilitar
          </p>
          <ul className="ml-4 list-disc space-y-1 text-[#162744]/80 dark:text-white/80">
            {impedimentos.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        </div>
      )}

      {error && (
        <p className="text-xs" role="alert" style={{ color: '#C2410C' }}>
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
