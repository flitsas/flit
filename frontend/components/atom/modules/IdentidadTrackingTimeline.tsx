'use client';

import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { Hito } from '@/components/operacion/IdentidadLecturaHumana';
import { hitosDeIdentidad, type HitoIdentidad } from '@/lib/tramites/identidad-lectura';
import type { BiometricValidation, IdentityAuditEvent } from '@/lib/api/types/procedure-runtime';

/** Hitos a la vista antes de colapsar los anteriores. */
export const HITOS_VISIBLES = 5;

/**
 * Numera los intentos: cada primer envío del enlace (`inicioCiclo`) abre uno. Los hitos llegan del más reciente al
 * más antiguo, así que se recorre al revés. Sin ningún inicio en la lista (p. ej. validación reutilizada), todo es el intento 1.
 */
export function intentosPorHito(hitos: readonly HitoIdentidad[]): number[] {
  const numeros = new Array<number>(hitos.length);
  let intento = 1;
  let abierto = false;
  for (let i = hitos.length - 1; i >= 0; i--) {
    if (hitos[i].inicioCiclo) {
      if (abierto) intento += 1;
      abierto = true;
    }
    numeros[i] = intento;
  }
  return numeros;
}

/**
 * Línea de tiempo de la identidad con el MISMO diseño del trámite (`Hito`), para el módulo de Validaciones.
 * Con más de {@link HITOS_VISIBLES} hitos muestra los recientes y deja los anteriores tras un botón, agrupados por intento.
 */
export function IdentidadHitosTimeline({ hitos }: { hitos: readonly HitoIdentidad[] }) {
  const [abierto, setAbierto] = useState(false);
  const intentos = useMemo(() => intentosPorHito(hitos), [hitos]);
  const hayIntentos = Math.max(0, ...intentos) > 1;
  const colapsable = hitos.length > HITOS_VISIBLES;
  const visibles = abierto || !colapsable ? hitos : hitos.slice(0, HITOS_VISIBLES);

  return (
    <div>
      {colapsable ? (
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <p className="text-xs text-flit-primary/70 dark:text-white/60" aria-live="polite">
            {abierto ? `${hitos.length} hitos, del más reciente al más antiguo` : `Últimos ${HITOS_VISIBLES} de ${hitos.length} hitos`}
          </p>
          <button
            type="button"
            onClick={() => setAbierto((a) => !a)}
            aria-expanded={abierto}
            className="rounded-full border border-border px-3 py-1.5 text-xs font-semibold text-flit-brand-ink focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
          >
            {abierto ? 'Ver solo los recientes' : `Ver los ${hitos.length - HITOS_VISIBLES} anteriores`}
          </button>
        </div>
      ) : null}
      <ol
        aria-label="Historial de la validación"
        className="relative space-y-3 border-l pl-4"
        style={{ borderColor: 'var(--border)' }}
      >
        {visibles.map((hito, i) => (
          <Fragment key={hito.id}>
            {abierto && hayIntentos && (i === 0 || intentos[i] !== intentos[i - 1]) ? (
              <li aria-hidden="true" className="pt-1">
                <span className="-ml-1.5 inline-block rounded-full border border-border bg-background px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
                  Intento {intentos[i]}
                </span>
              </li>
            ) : null}
            <Hito hito={hito} vigente={i === 0} />
          </Fragment>
        ))}
      </ol>
    </div>
  );
}

/**
 * Tracking de una validación en el detalle de Validaciones: carga la bitácora y la cuenta como línea de tiempo.
 * Reemplaza a la tabla técnica; mismos orígenes de datos (ruta normal o de red) y misma recarga por `refreshKey`.
 */
export function IdentidadTrackingTimeline({
  validation,
  tenantId,
  refreshKey = 0,
  network = false,
}: {
  validation: BiometricValidation;
  tenantId?: string | null;
  refreshKey?: number;
  network?: boolean;
}) {
  const [eventos, setEventos] = useState<IdentityAuditEvent[] | null>(null);
  const [referenciada, setReferenciada] = useState(false);
  const [cargando, setCargando] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reintento, setReintento] = useState(0);

  const cargar = useCallback(async () => {
    try {
      const res = network
        ? await tramitesClient.getNetworkIdentityAudit(validation.id)
        : await tramitesClient.getBiometricAuditByValidation(validation.id, tenantId ?? undefined);
      return { eventos: res.events ?? [], referenciada: res.referencedFromOtherProcedure ?? false, error: null };
    } catch (err) {
      return {
        eventos: null,
        referenciada: false,
        error: err instanceof Error ? err.message : 'No se pudo cargar el historial.',
      };
    }
  }, [validation.id, tenantId, network]);

  useEffect(() => {
    let cancelado = false;
    void (async () => {
      const res = await cargar();
      if (cancelado) return;
      // Un fallo del poll no borra lo ya mostrado: solo se reemplaza con datos nuevos.
      if (res.eventos) setEventos(res.eventos);
      setReferenciada(res.referenciada);
      setError(res.error);
      setCargando(false);
    })();
    return () => {
      cancelado = true;
    };
  }, [cargar, refreshKey, reintento]);

  const hitos = useMemo(() => hitosDeIdentidad(validation, eventos ?? []), [validation, eventos]);

  if (cargando) {
    return (
      <p className="text-xs opacity-60" role="status" aria-live="polite">
        Cargando el historial…
      </p>
    );
  }

  return (
    <div className="space-y-2">
      {error ? (
        <div role="alert">
          <p className="text-xs" style={{ color: 'var(--destructive)' }}>
            No se pudo cargar el historial de esta validación.
          </p>
          <button
            type="button"
            onClick={() => {
              setCargando(true);
              setReintento((n) => n + 1);
            }}
            className="mt-1 text-xs font-semibold text-flit-brand-ink underline underline-offset-2 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
          >
            Reintentar
          </button>
        </div>
      ) : null}
      {referenciada ? (
        <p
          className="rounded-lg px-2.5 py-1.5 text-xs"
          style={{ background: 'var(--di-info-soft)', color: 'var(--di-action)' }}
          role="status"
        >
          Validación reutilizada de otro trámite del mismo cliente (identidad ya verificada). El historial corresponde a
          ese trámite.
        </p>
      ) : null}
      {!error && hitos.length === 0 ? (
        <p className="text-xs opacity-60">Todavía no hay movimientos que mostrar.</p>
      ) : hitos.length > 0 ? (
        <IdentidadHitosTimeline hitos={hitos} />
      ) : null}
    </div>
  );
}
