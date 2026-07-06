'use client';

import { ChevronRight, Car, ArrowLeftRight } from 'lucide-react';
import { TramitesTable } from './TramitesTable';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * M0 — Entrada por MODALIDAD (desligada de Parametrización). El gestor elige
 * Matrícula inicial o Traspaso y la vista solo muestra el chooser + el listado.
 * Track B: ya NO crea la instancia ni hospeda el wizard inline; delega en la
 * ruta (onStartTramite → /tramites/nuevo/[modalidad]) la creación + navegación.
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

interface OperacionViewProps {
  /**
   * Track B — el listado ya no hospeda el wizard inline. Al elegir modalidad,
   * delega en el contenedor (ruta) para navegar a /tramites/nuevo/[modalidad].
   */
  onStartTramite: (modalidad: WizardModalidad) => void;
}

export function OperacionView({ onStartTramite }: OperacionViewProps) {
  return (
    <div className="flex flex-col gap-4">
      <section
        className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14]"
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
                  onClick={() => onStartTramite(m.id)}
                  className="w-full h-full flex flex-col gap-3 px-4 py-4 rounded-xl border text-left transition hover:border-[#557EFF]"
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

      {/* Listado de instancias (Track A). La tabla hospeda su propia sección,
          toolbar de filtros y encabezado con contador dinámico. Carga al montar;
          como ahora es una ruta propia, volver del wizard remonta y refresca. */}
      <TramitesTable />
    </div>
  );
}
