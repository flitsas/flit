'use client';

import { useCallback, useEffect, useState } from 'react';
import { superadminClient } from '@/lib/api/superadmin-client';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

type Status = 'idle' | 'loading' | 'success' | 'error';

export function useProcedureTypes() {
  const [items, setItems] = useState<ProcedureTypeSummary[]>([]);
  // Estado inicial 'loading': la carga de montaje arranca cargando sin setState síncrono en el efecto.
  const [status, setStatus] = useState<Status>('loading');
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      const data = await superadminClient.listProcedureTypes();
      setItems(data);
      setStatus('success');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al cargar parametrizaciones');
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    // Carga inicial: setState ocurre solo en callbacks async, no de forma síncrona en el efecto.
    let active = true;
    superadminClient
      .listProcedureTypes()
      .then((data) => {
        if (!active) return;
        setItems(data);
        setStatus('success');
      })
      .catch((err) => {
        if (!active) return;
        setError(err instanceof Error ? err.message : 'Error al cargar parametrizaciones');
        setStatus('error');
      });
    return () => {
      active = false;
    };
  }, []);

  const publish = useCallback(
    async (id: string) => {
      try {
        const updated = await superadminClient.publish(id);
        setItems((prev) => prev.map((it) => (it.id === id ? updated : it)));
        return updated;
      } catch (err) {
        throw err instanceof Error ? err : new Error('Error al publicar');
      }
    },
    [],
  );

  return { items, status, error, reload: load, publish };
}
