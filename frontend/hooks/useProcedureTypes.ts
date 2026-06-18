'use client';

import { useCallback, useEffect, useState } from 'react';
import { superadminClient } from '@/lib/api/superadmin-client';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

type Status = 'idle' | 'loading' | 'success' | 'error';

export function useProcedureTypes() {
  const [items, setItems] = useState<ProcedureTypeSummary[]>([]);
  const [status, setStatus] = useState<Status>('idle');
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
    load();
  }, [load]);

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
