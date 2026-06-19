'use client';

import { useState } from 'react';
import { ChevronRight } from 'lucide-react';
import { useProcedureConfiguration } from '@/hooks/useProcedureConfiguration';
import { TramiteWizard } from './TramiteWizard';
import {
  OperacionEmptyState,
  OperacionErrorState,
  OperacionLoadingState,
} from './states';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

const FAMILY_LABELS: Record<string, string> = {
  MATRICULAS: 'Matrículas',
  TRASPASO: 'Traspaso',
  OTROS: 'Otros Trámites',
};

export function OperacionView() {
  const {
    items,
    status,
    error,
    reload,
    configuration,
    configStatus,
    configError,
    loadConfiguration,
    clearConfiguration,
  } = useProcedureConfiguration();

  const [selected, setSelected] = useState<ProcedureTypeSummary | null>(null);

  const handleSelect = async (type: ProcedureTypeSummary) => {
    setSelected(type);
    await loadConfiguration(type.code);
  };

  const handleExit = () => {
    setSelected(null);
    clearConfiguration();
    void reload();
  };

  // Wizard activo: hay config cargada para el tipo elegido.
  if (selected && configuration && configStatus === 'success') {
    return (
      <TramiteWizard
        configuration={configuration}
        procedureTypeId={configuration.id}
        onExit={handleExit}
      />
    );
  }

  return (
    <div className="flex-1 min-h-0 flex flex-col gap-4 overflow-y-auto">
      <section
        className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14]"
        style={{ borderColor: '#DFE5ED' }}
      >
        <h2 className="text-sm font-bold mb-1">Iniciar nuevo trámite</h2>
        <p className="text-[11px] opacity-60 mb-4">
          Selecciona un tipo de trámite publicado para comenzar.
        </p>

        {status === 'loading' && <OperacionLoadingState />}
        {status === 'error' && error && (
          <OperacionErrorState message={error} onRetry={reload} />
        )}
        {status === 'success' && items.length === 0 && <OperacionEmptyState />}

        {status === 'success' && items.length > 0 && (
          <>
            {configStatus === 'error' && configError && (
              <div
                className="rounded-xl p-3 text-xs border mb-3"
                style={{
                  borderColor: '#FF4E00',
                  background: 'rgba(255,78,0,0.06)',
                  color: '#FF4E00',
                }}
                role="alert"
                aria-live="polite"
              >
                {configError}
              </div>
            )}
            <ul
              className="space-y-2"
              role="list"
              aria-label="Tipos de trámite publicados"
            >
              {items.map((type) => {
                const isLoading =
                  selected?.id === type.id && configStatus === 'loading';
                return (
                  <li key={type.id}>
                    <button
                      type="button"
                      onClick={() => void handleSelect(type)}
                      disabled={configStatus === 'loading'}
                      className="w-full flex items-center justify-between gap-3 px-4 py-3 rounded-xl border text-left text-xs transition disabled:opacity-60"
                      style={{ borderColor: '#DFE5ED' }}
                      aria-label={`Iniciar ${type.name}`}
                    >
                      <div className="min-w-0">
                        <p className="font-semibold truncate">{type.name}</p>
                        <p className="opacity-60 mt-0.5">
                          <span className="font-mono">{type.code}</span>
                          {' · '}
                          {FAMILY_LABELS[type.family] ?? type.family}
                        </p>
                      </div>
                      <span
                        className="shrink-0 flex items-center gap-1 font-semibold"
                        style={{ color: '#557EFF' }}
                      >
                        {isLoading ? 'Cargando…' : 'Iniciar'}
                        <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </>
        )}
      </section>

      {/* Listado de instancias — FUERA DE ALCANCE de #10200 (placeholder). */}
      <section
        className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14] shrink-0"
        style={{ borderColor: '#DFE5ED' }}
      >
        <div className="flex items-center justify-between mb-2">
          <h2 className="text-sm font-bold">Trámites en curso</h2>
          <span
            className="px-2 py-0.5 rounded text-[9px] font-bold text-white"
            style={{ background: '#557EFF' }}
          >
            Próximamente
          </span>
        </div>
        <p className="text-[11px] opacity-60">
          El listado y seguimiento de instancias se habilita en una próxima
          entrega.
        </p>
      </section>
    </div>
  );
}
