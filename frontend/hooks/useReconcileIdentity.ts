'use client';

import { useCallback, useState } from 'react';
import type { ReconcileIdentityResult } from '@/lib/api/types/procedure-runtime';

/**
 * «Actualizar estado» consultando al proveedor de identidad (Kyverum Verify): lógica compartida entre
 * el wizard (`ReconcileAction`, una validación por la ruta del trámite) y el detalle de persona del
 * módulo Validaciones (varias validaciones por la ruta por `validationId`). El hook no conoce la ruta:
 * recibe la función que reconcilia.
 */

export const MENSAJE_NO_RESUELVE =
  'El proveedor aún no resuelve la validación. Intenta de nuevo en unos segundos.';
export const MENSAJE_ERROR_RECONCILIAR = 'No se pudo actualizar el estado.';
export const MENSAJE_ACTUALIZADO = 'Estado actualizado.';
export const MENSAJE_ALGUNA_FALLO = 'Alguna validación no se pudo consultar al proveedor.';

/** Tope de validaciones que se consultan por clic (evita ráfagas contra el proveedor). */
export const MAX_RECONCILIAR_POR_CLIC = 5;

export type ReconcileMessageTone = 'success' | 'info' | 'error';

export interface ReconcileMessage {
  tone: ReconcileMessageTone;
  text: string;
}

export interface ReconcileSummary {
  updated: number;
  pending: number;
  failed: number;
}

function mensajeDeError(err: unknown): string {
  return err instanceof Error && err.message ? err.message : MENSAJE_ERROR_RECONCILIAR;
}

/** Mensaje único para varias consultas: prioriza el éxito, luego «aún no resuelve», luego el error. */
function mensajeAgregado(summary: ReconcileSummary, primerError: unknown): ReconcileMessage {
  const sufijo = summary.failed > 0 ? ` ${MENSAJE_ALGUNA_FALLO}` : '';
  if (summary.updated > 0) return { tone: 'success', text: `${MENSAJE_ACTUALIZADO}${sufijo}` };
  if (summary.pending > 0) return { tone: 'info', text: `${MENSAJE_NO_RESUELVE}${sufijo}` };
  return { tone: 'error', text: mensajeDeError(primerError) };
}

export function useReconcileIdentity() {
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<ReconcileMessage | null>(null);

  /**
   * Una validación (wizard). `updated=true` no deja mensaje (quien llama refresca la vista);
   * `updated=false` informa que el proveedor aún no resuelve; un error muestra su `detail`.
   * Devuelve el resultado o `null` si falló.
   */
  const run = useCallback(
    async (reconcile: () => Promise<ReconcileIdentityResult>): Promise<ReconcileIdentityResult | null> => {
      setMessage(null);
      setSubmitting(true);
      try {
        const res = await reconcile();
        if (!res.updated) setMessage({ tone: 'info', text: MENSAJE_NO_RESUELVE });
        return res;
      } catch (err) {
        setMessage({ tone: 'error', text: mensajeDeError(err) });
        return null;
      } finally {
        setSubmitting(false);
      }
    },
    [],
  );

  /**
   * Varias validaciones (detalle de persona): en secuencia, con tope {@link MAX_RECONCILIAR_POR_CLIC},
   * y un único mensaje agregado.
   */
  const runMany = useCallback(
    async (
      validationIds: readonly string[],
      reconcileOne: (validationId: string) => Promise<ReconcileIdentityResult>,
    ): Promise<ReconcileSummary> => {
      setMessage(null);
      setSubmitting(true);
      const summary: ReconcileSummary = { updated: 0, pending: 0, failed: 0 };
      let primerError: unknown = null;
      try {
        for (const id of validationIds.slice(0, MAX_RECONCILIAR_POR_CLIC)) {
          try {
            const res = await reconcileOne(id);
            if (res.updated) summary.updated += 1;
            else summary.pending += 1;
          } catch (err) {
            if (summary.failed === 0) primerError = err;
            summary.failed += 1;
          }
        }
        setMessage(mensajeAgregado(summary, primerError));
        return summary;
      } finally {
        setSubmitting(false);
      }
    },
    [],
  );

  const clearMessage = useCallback(() => setMessage(null), []);

  return { submitting, message, run, runMany, clearMessage };
}
