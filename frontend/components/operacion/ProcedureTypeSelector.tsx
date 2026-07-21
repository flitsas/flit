'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { OperatorProcedureType } from '@/lib/api/types/procedure-runtime';

interface ProcedureTypeSelectorProps {
  onSelect: (code: string) => void;
  tenantId?: string;
}

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; types: OperatorProcedureType[] };

/**
 * FEATURE-08 / HU-FE-06 (CFD-12) — selector de tipo de trámite para el botón único de creación.
 * Consume GET /api/v1/procedure-types (solo publicados) y expone 4 estados de UI: cargando, error,
 * vacío y cargado. Al elegir un tipo emite su <code>code</code> para crear la instancia.
 */
export function ProcedureTypeSelector({ onSelect, tenantId }: ProcedureTypeSelectorProps) {
  const [state, setState] = useState<LoadState>({ kind: 'loading' });

  useEffect(() => {
    let active = true;
    // Sin setState síncrono: el primer setState ocurre TRAS el await (patrón EstadoTimeline),
    // así el rechazo queda manejado dentro del ciclo async sin escapar del act de RTL.
    const load = async () => {
      try {
        const types = await tramitesClient.getProcedureTypes(tenantId);
        if (active) setState({ kind: 'loaded', types });
      } catch {
        if (active)
          setState({ kind: 'error', message: 'No se pudieron cargar los tipos de trámite.' });
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [tenantId]);

  if (state.kind === 'loading') {
    return (
      <div aria-busy="true" aria-label="Cargando tipos de trámite" className="text-xs opacity-60">
        Cargando tipos de trámite…
      </div>
    );
  }

  if (state.kind === 'error') {
    return (
      <p role="alert" className="text-xs font-medium" style={{ color: '#FF4E00' }}>
        {state.message}
      </p>
    );
  }

  if (state.types.length === 0) {
    return <p className="text-xs opacity-50">No hay tipos de trámite publicados disponibles.</p>;
  }

  return (
    <ul aria-label="Tipos de trámite disponibles" className="grid grid-cols-1 sm:grid-cols-2 gap-3">
      {state.types.map((type) => (
        <li key={type.id}>
          <button
            type="button"
            onClick={() => onSelect(type.code)}
            aria-label={`Iniciar trámite ${type.name}`}
            className="w-full text-left rounded-2xl p-4 border transition hover:border-[#557EFF]"
          >
            <p className="text-sm font-bold">{type.name}</p>
            <p className="text-[11px] opacity-60">{type.family}</p>
          </button>
        </li>
      ))}
    </ul>
  );
}
