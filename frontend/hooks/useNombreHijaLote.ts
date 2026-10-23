'use client';

import { useEffect, useState } from 'react';
import { fetchNetworkChildren } from '@/lib/api/tramites-client';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';

/**
 * HU #13419 AC6 (épica #13216) — nombre de la compañía hija de un lote de red acotado.
 *
 * El contrato trae `alcanceHijaId` (uuid), no el nombre: se resuelve contra la lista de la vista de
 * red (`GET /api/v1/tramites/network/children`, la misma del selector de alcance). Solo se pide para
 * lotes `alcanceRed === 'hija'`, una vez por id de hija mientras el aviso esté montado, con
 * `AbortSignal`. Así el rótulo sobrevive a una recarga (el lote llega por `/actual`). Devuelve `null`
 * mientras carga, si la hija ya no está en la red o si la lista falla (403/5xx/red): el aviso cae a
 * «Red · compañía de la red».
 *
 * Uso de ejemplo:
 *   const nombreHija = useNombreHijaLote(lote); // 'Concesionario Hijo SAS' | null
 */
export function useNombreHijaLote(lote: LoteConsolidados | null | undefined): string | null {
  const hijaId = lote?.alcanceRed === 'hija' ? (lote.alcanceHijaId ?? null) : null;
  const [resuelto, setResuelto] = useState<{ hijaId: string; nombre: string | null } | null>(null);

  useEffect(() => {
    if (!hijaId) return;
    const ctrl = new AbortController();
    fetchNetworkChildren(ctrl.signal).then(
      (hijas) => {
        if (ctrl.signal.aborted) return;
        const nombre = (hijas ?? []).find((h) => h.id === hijaId)?.nombre?.trim() || null;
        setResuelto({ hijaId, nombre });
      },
      () => {
        if (!ctrl.signal.aborted) setResuelto({ hijaId, nombre: null });
      },
    );
    return () => ctrl.abort();
  }, [hijaId]);

  return hijaId && resuelto?.hijaId === hijaId ? resuelto.nombre : null;
}
