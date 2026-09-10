'use client';

import { useEffect, useRef, useState } from 'react';
import { FileText } from 'lucide-react';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { WizardAccordion } from './WizardAccordion';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { ActiveDeed } from '@/lib/api/types/procedure-runtime';

/**
 * Umbral de vigencia (días restantes) → tono semántico del badge (HU #10906).
 * ≤ 7 días: rojo (danger) · ≤ 30 días: ámbar (warning) · resto: verde (success).
 */
export function deedVigenciaTone(diasRestantes: number): StatusTone {
  if (diasRestantes <= 7) return 'danger';
  if (diasRestantes <= 30) return 'warning';
  return 'success';
}

/** Etiqueta del badge de vigencia por días restantes. */
export function deedVigenciaLabel(diasRestantes: number): string {
  if (diasRestantes <= 0) return 'Vence hoy';
  if (diasRestantes === 1) return '1 día';
  return `${diasRestantes} días`;
}

export interface ActiveDeedsState {
  loading: boolean;
  deeds: ActiveDeed[] | null;
  error: string | null;
}

/**
 * Carga perezosa de las escrituras vigentes de la compañía (HU #10906): se pide la primera vez que
 * `active` se pone a true y el resultado se conserva.
 *
 * El hook lo llama el CONTENEDOR (el acordeón, el carril), no la lista: tanto el acordeón como el
 * panel lateral desmontan su contenido al cerrarse, y con el estado dentro de la lista cada
 * reapertura disparaba una consulta nueva.
 */
export function useActiveDeeds(tenantId: string | undefined, active: boolean): ActiveDeedsState {
  // Un solo estado con el resultado: `loading` se DERIVA del render (visible y aún sin resultado),
  // de modo que el efecto no tiene que marcar el arranque con un setState síncrono.
  const [result, setResult] = useState<{ items: ActiveDeed[] | null; error: string | null } | null>(
    null,
  );
  // El disparo se marca en un ref, no en el estado: entre que arranca la consulta y llega el
  // resultado hay renders en los que `result` sigue a null, y sin esta marca el doble montaje de
  // StrictMode lanzaría una segunda consulta.
  const started = useRef(false);

  useEffect(() => {
    if (!active || started.current) return;
    started.current = true;
    let alive = true;
    void tramitesClient
      .fetchActiveDeeds(tenantId)
      .then((items) => {
        if (alive) setResult({ items, error: null });
      })
      .catch((err: unknown) => {
        if (alive) {
          setResult({
            items: null,
            error: err instanceof Error ? err.message : 'No se pudieron cargar las escrituras.',
          });
        }
      });
    return () => {
      alive = false;
    };
  }, [active, tenantId]);

  return {
    loading: active && result === null,
    deeds: result?.items ?? null,
    error: result?.error ?? null,
  };
}

/**
 * Listado de escrituras vigentes. Presentacional: recibe el estado de {@link useActiveDeeds} para
 * poder mostrarse tanto en el acordeón del paso como en el panel lateral del carril de consulta.
 */
export function ActiveDeedsList({ loading, deeds, error }: ActiveDeedsState) {
  return (
        <>
          {loading && (
            <p className="text-xs opacity-70" role="status" aria-live="polite">
              Cargando escrituras…
            </p>
          )}
          {error && (
            <p className="text-xs" style={{ color: '#FF4E00' }} role="alert" aria-live="polite">
              {error}
            </p>
          )}
          {!loading && !error && deeds !== null && deeds.length === 0 && (
            <p className="text-xs opacity-70" role="status">
              La compañía no tiene escrituras vigentes.
            </p>
          )}
          {!loading && !error && deeds !== null && deeds.length > 0 && (
            <ul className="space-y-3" aria-label="Escrituras vigentes">
              {deeds.map((deed) => (
                <li
                  // Llave estable por escritura: el backend devuelve UNA fila por cada par
                  // (escritura × compañía), de modo que una misma compañía (NIT) puede aparecer en
                  // varias filas —una por escritura vigente— (Feature #10929).
                  key={deed.id}
                  className="flex items-start justify-between gap-3 rounded-2xl border bg-white px-4 py-3.5 dark:bg-[#162744]"
                >
                  <div className="min-w-0">
                    <p className="text-xs font-semibold" style={{ color: '#162744' }}>
                      {deed.name}
                    </p>
                    <p className="mt-0.5 text-xs opacity-60">NIT {deed.nit}</p>
                    {deed.representativeName ? (
                      <p className="text-xs opacity-60">
                        RL: {deed.representativeName}
                        {deed.representativeDocumentType && deed.representativeDocumentNumber
                          ? ` · ${deed.representativeDocumentType} ${deed.representativeDocumentNumber}`
                          : ''}
                      </p>
                    ) : (
                      <p className="text-xs opacity-50">Sin RL vinculado</p>
                    )}
                    {deed.description && (
                      <p className="mt-1 text-xs font-medium" style={{ color: '#162744' }}>
                        {deed.description}
                      </p>
                    )}
                  </div>
                  <StatusBadge
                    tone={deedVigenciaTone(deed.diasRestantes)}
                    label={deedVigenciaLabel(deed.diasRestantes)}
                    ariaLabel={`Vigencia: ${deedVigenciaLabel(deed.diasRestantes)} restantes`}
                  />
                </li>
              ))}
            </ul>
          )}
        </>
  );
}

/**
 * Collapse de escrituras vigentes de la compañía en el PRIMER paso del wizard (HU #10906).
 * Contraído por defecto; carga perezosa al abrir (`GET /api/v1/tramites/deeds/active`, tenant-scoped
 * por el header). Mismo patrón de disclosure lazy que la bitácora de BiometricStep.
 */
export function ActiveDeedsCollapse({ tenantId }: { tenantId?: string }) {
  const [open, setOpen] = useState(false);
  // El estado vive aquí, no en la lista: el acordeón desmonta su contenido al cerrarse.
  const state = useActiveDeeds(tenantId, open);

  return (
    <WizardAccordion
      title="Escrituras vigentes de la compañía"
      icon={<FileText className="h-4 w-4 shrink-0" style={{ color: '#557EFF' }} aria-hidden="true" />}
      open={open}
      onOpenChange={setOpen}
    >
      <ActiveDeedsList {...state} />
    </WizardAccordion>
  );
}
