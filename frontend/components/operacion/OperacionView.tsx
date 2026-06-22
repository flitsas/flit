'use client';

import { useState } from 'react';
import { ChevronRight, Car, ArrowLeftRight } from 'lucide-react';
import { TramiteWizard } from './TramiteWizard';
import { TramitesTable } from './TramitesTable';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * M0 — Entrada por MODALIDAD (desligada de Parametrización). En vez de listar
 * tipos publicados, el gestor elige Matrícula inicial o Traspaso; el backend
 * deriva la tipología desde la modalidad al crear la instancia (POST /instances
 * con `modalidad`). El wizard es server-driven, así que NO se necesita cargar la
 * configuración del tipo aquí: basta la modalidad para el header y la creación.
 */
interface Modalidad {
  id: WizardModalidad;
  label: string;
  description: string;
  icon: typeof Car;
}

const MODALIDADES: Modalidad[] = [
  {
    id: 'matricula_inicial',
    label: 'Matrícula inicial',
    description: 'Registra por primera vez un vehículo nuevo ante el organismo de tránsito.',
    icon: Car,
  },
  {
    id: 'traspaso',
    label: 'Traspaso estándar',
    description: 'Transfiere la propiedad de un vehículo entre vendedor y comprador.',
    icon: ArrowLeftRight,
  },
];

export function OperacionView() {
  const [selected, setSelected] = useState<Modalidad | null>(null);
  // Se incrementa al volver del wizard para forzar el refetch de la tabla:
  // así, tras "Enviar a tránsito", el listado refleja el trámite nuevo/actualizado.
  const [refreshKey, setRefreshKey] = useState(0);

  const handleExit = () => {
    setSelected(null);
    setRefreshKey((k) => k + 1);
  };

  // Wizard activo: hay una modalidad elegida. El wizard crea la instancia.
  if (selected) {
    return (
      <TramiteWizard
        modalidad={selected.id}
        title={selected.label}
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
          Selecciona la modalidad del trámite para comenzar.
        </p>

        <ul
          className="grid grid-cols-1 sm:grid-cols-2 gap-3"
          role="list"
          aria-label="Modalidades de trámite"
        >
          {MODALIDADES.map((m) => {
            const Icon = m.icon;
            return (
              <li key={m.id}>
                <button
                  type="button"
                  onClick={() => setSelected(m)}
                  className="w-full h-full flex flex-col gap-3 px-4 py-4 rounded-xl border text-left transition hover:border-[#557EFF]"
                  style={{ borderColor: '#DFE5ED' }}
                  aria-label={`Iniciar ${m.label}`}
                >
                  <span
                    className="h-10 w-10 rounded-xl grid place-items-center"
                    style={{ background: 'rgba(85,126,255,0.10)', color: '#557EFF' }}
                    aria-hidden="true"
                  >
                    <Icon className="h-5 w-5" />
                  </span>
                  <span className="min-w-0">
                    <span className="block text-sm font-semibold">{m.label}</span>
                    <span className="block text-[11px] opacity-60 mt-0.5">
                      {m.description}
                    </span>
                  </span>
                  <span
                    className="mt-auto flex items-center gap-1 text-xs font-semibold"
                    style={{ color: '#557EFF' }}
                  >
                    Iniciar
                    <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      </section>

      {/* Listado de instancias (Slice M6) — se refresca al volver del wizard. */}
      <section
        className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14] shrink-0"
        style={{ borderColor: '#DFE5ED' }}
      >
        <h2 className="text-sm font-bold mb-3">Trámites en curso</h2>
        <TramitesTable refreshKey={refreshKey} />
      </section>
    </div>
  );
}
