'use client';

import { useCallback, useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import type { ProcedureConfiguration } from '@/lib/api/types/procedure-runtime';

type Status = 'idle' | 'loading' | 'success' | 'error';

/**
 * Carga la lista de tipos de trámite PUBLISHED (AC1) y, dado un code,
 * su configuración dinámica de steps/sections/fields (AC2).
 */
export function useProcedureConfiguration() {
  const [items, setItems] = useState<ProcedureTypeSummary[]>([]);
  const [status, setStatus] = useState<Status>('loading');
  const [error, setError] = useState<string | null>(null);

  const [configuration, setConfiguration] = useState<ProcedureConfiguration | null>(null);
  const [configStatus, setConfigStatus] = useState<Status>('idle');
  const [configError, setConfigError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      const data = await tramitesClient.listPublishedProcedureTypes();
      setItems(data);
      setStatus('success');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al cargar tipos de trámite');
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    let active = true;
    tramitesClient
      .listPublishedProcedureTypes()
      .then((data) => {
        if (!active) return;
        setItems(data);
        setStatus('success');
      })
      .catch((err) => {
        if (!active) return;
        setError(err instanceof Error ? err.message : 'Error al cargar tipos de trámite');
        setStatus('error');
      });
    return () => {
      active = false;
    };
  }, []);

  const loadConfiguration = useCallback(async (code: string) => {
    setConfigStatus('loading');
    setConfigError(null);
    setConfiguration(null);
    try {
      const config = await tramitesClient.getConfiguration(code);
      setConfiguration(config);
      setConfigStatus('success');
      return config;
    } catch (err) {
      setConfigError(
        err instanceof Error ? err.message : 'Error al cargar la configuración',
      );
      setConfigStatus('error');
      return null;
    }
  }, []);

  const clearConfiguration = useCallback(() => {
    setConfiguration(null);
    setConfigStatus('idle');
    setConfigError(null);
  }, []);

  return {
    items,
    status,
    error,
    reload: load,
    configuration,
    configStatus,
    configError,
    loadConfiguration,
    clearConfiguration,
  };
}
