'use client';

import { useCallback, useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { ProcedureFamily, ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import { FAMILY_ORDER } from '@/lib/api/types/procedure-parametrization';

type Status = 'loading' | 'success' | 'error';

/** Familia con los tipos que el gestor puede elegir dentro de ella. */
export interface FamiliaConTipos {
  family: ProcedureFamily;
  tipos: ProcedureTypeSummary[];
}

/**
 * Catálogo de tipos OPERABLES para crear un trámite (ADR-0050), agrupados por familia.
 *
 * Filtra por `wizardEnabled`, no por `publicationStatus`: un tipo publicado es visible en
 * administración, pero solo es elegible al crear cuando tiene recorrido, documentos y causales. Sin
 * ese filtro el selector prometería flujos que no existen, que es justo lo que la barrera evita.
 *
 * Las familias salen de lo que devuelve el catálogo, no de una lista fija: una familia sin tipos
 * habilitados sencillamente no aparece.
 */
export function useTiposHabilitados() {
  const [familias, setFamilias] = useState<FamiliaConTipos[]>([]);
  const [status, setStatus] = useState<Status>('loading');
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      const data = await tramitesClient.listPublishedProcedureTypes();
      const habilitados = data.filter((t) => t.wizardEnabled);

      const agrupadas = FAMILY_ORDER.map((family) => ({
        family,
        tipos: habilitados
          .filter((t) => t.family === family)
          .sort((a, b) => a.name.localeCompare(b.name, 'es')),
      })).filter((g) => g.tipos.length > 0);

      setFamilias(agrupadas);
      setStatus('success');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No fue posible cargar los tipos de trámite');
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return { familias, status, error, reload: load };
}
