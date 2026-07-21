'use client';

import { useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { useProcedureInstance } from '@/hooks/useProcedureInstance';
import { ProcedureTypeSelector } from '@/components/operacion/ProcedureTypeSelector';

/**
 * FEATURE-08 / HU-FE-06 (CFD-12) — botón único: selector de tipo de trámite. Al elegir un tipo
 * publicado se crea la instancia draft por <code>procedureTypeCode</code> y se redirige al wizard.
 */
export default function NuevoTramiteSelectorPage() {
  const router = useRouter();
  const { state, start } = useProcedureInstance();

  const handleSelect = useCallback(
    async (code: string) => {
      const summary = await start({ procedureTypeCode: code });
      if (summary) router.replace(`/tramites/${summary.id}`);
    },
    [router, start],
  );

  return (
    <div className="px-6 pt-6 pb-24 space-y-5">
      <header>
        <h1 className="text-xl font-bold">Nuevo trámite</h1>
        <p className="text-xs opacity-60">Selecciona el tipo de trámite que deseas iniciar.</p>
      </header>

      {state.error && (
        <p role="alert" className="text-xs font-medium" style={{ color: '#FF4E00' }}>
          {state.error}
        </p>
      )}

      <ProcedureTypeSelector onSelect={handleSelect} />
    </div>
  );
}
