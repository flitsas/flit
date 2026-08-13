'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeftRight, Car, ChevronRight } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { WIZARD_CARD, WIZARD_LABEL, WIZARD_SELECT } from '@/components/operacion/wizard-field-styles';
import type { WizardModalidad } from '@/lib/api/types/procedure-runtime';

/**
 * Entrada al wizard SIN modalidad — paso 1 del flujo del diseño.
 *
 * En la propuesta, "Nuevo trámite" entra directo al asistente y el tipo se elige DENTRO del
 * primer paso con el select "Trámite Principal", no en un diálogo previo. Esta página es ese
 * primer tramo: en cuanto hay tipo, continúa a `/tramites/nuevo/[modalidad]`, que es donde vive
 * la consulta del vehículo.
 *
 * No crea nada: igual que la ruta con modalidad, el trámite se da de alta al pulsar "Continuar"
 * en el paso 1 (creación diferida). Aquí solo se resuelve qué modalidad gobierna ese paso.
 */

const OPCIONES: { id: WizardModalidad; label: string; icon: typeof Car; descripcion: string }[] = [
  {
    id: 'matricula_inicial',
    label: 'Matrícula inicial',
    icon: Car,
    descripcion: 'Primera matrícula del vehículo ante el organismo de tránsito.',
  },
  {
    id: 'traspaso',
    label: 'Traspaso estándar',
    icon: ArrowLeftRight,
    descripcion: 'Cambio de propietario de un vehículo ya matriculado.',
  },
];

export default function NuevoTramiteTipoPage() {
  const router = useRouter();
  const [modalidad, setModalidad] = useState<WizardModalidad | ''>('');
  // Bloqueo por compañía: mismas banderas que aplica la ruta con modalidad, para no ofrecer un
  // tipo que el backend va a rechazar al crear.
  const [bloqueo, setBloqueo] = useState<{ matricula: boolean; traspaso: boolean }>({
    matricula: false,
    traspaso: false,
  });

  useEffect(() => {
    let active = true;
    void tramitesClient
      .getConsultationConfig()
      .then((cfg) => {
        if (!active) return;
        const block = cfg.blockProcedureFamily;
        setBloqueo({
          matricula: block?.matriculas ?? false,
          traspaso: block?.traspaso ?? false,
        });
      })
      .catch(() => {
        // Sin config legible no se bloquea nada: el backend corta al crear si aplica.
      });
    return () => {
      active = false;
    };
  }, []);

  const bloqueada = (id: WizardModalidad) =>
    id === 'matricula_inicial' ? bloqueo.matricula : bloqueo.traspaso;
  const todasBloqueadas = bloqueo.matricula && bloqueo.traspaso;

  return (
    <section className="flex min-w-0 flex-col gap-4">
      <div className="rounded-2xl border border-[#DFE5ED] bg-white px-5 py-3 dark:border-white/10 dark:bg-[#162744]">
        <h1 className="text-2xl font-bold leading-tight" style={{ color: '#557EFF' }}>
          Nuevo trámite
        </h1>
        <p className="mt-1 text-sm leading-snug text-[#162744]/70 dark:text-white/60">
          Elige el trámite principal para comenzar. El resto de datos se piden en los pasos
          siguientes.
        </p>
      </div>

      <div className={WIZARD_CARD}>
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
          <label className="block min-w-0">
            <span className={WIZARD_LABEL}>Trámite principal *</span>
            <select
              className={`mt-1 ${WIZARD_SELECT}`}
              value={modalidad}
              onChange={(e) => setModalidad(e.target.value as WizardModalidad | '')}
              aria-label="Trámite principal"
            >
              <option value="">Selecciona…</option>
              {OPCIONES.map((o) => (
                <option key={o.id} value={o.id} disabled={bloqueada(o.id)}>
                  {o.label}
                  {bloqueada(o.id) ? ' (no habilitado)' : ''}
                </option>
              ))}
            </select>
          </label>

          <div className="lg:col-span-2">
            <span className={WIZARD_LABEL}>Qué incluye</span>
            <p className="mt-1 text-xs leading-snug opacity-70">
              {modalidad
                ? OPCIONES.find((o) => o.id === modalidad)?.descripcion
                : 'Selecciona un trámite principal para ver su descripción.'}
            </p>
          </div>
        </div>

        {todasBloqueadas ? (
          <p className="mt-4 text-xs" style={{ color: '#FF4E00' }} role="alert">
            La compañía tiene bloqueada la creación de trámites. Contacta al administrador para
            habilitarla en la configuración de la compañía.
          </p>
        ) : null}
      </div>

      <div className="flex items-center justify-between gap-2 border-t pt-3">
        <button
          type="button"
          onClick={() => router.push('/tramites')}
          className="flex items-center gap-1 rounded-xl border px-4 py-2 text-xs font-medium transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          style={{ borderColor: '#162744', color: '#162744' }}
        >
          Cancelar
        </button>
        <button
          type="button"
          disabled={!modalidad || bloqueada(modalidad as WizardModalidad)}
          onClick={() => router.push(`/tramites/nuevo/${modalidad}`)}
          className="flex items-center gap-1 rounded-xl px-5 py-2 text-xs font-semibold text-white transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
          style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
        >
          Continuar
          <ChevronRight className="h-3 w-3" aria-hidden="true" />
        </button>
      </div>
    </section>
  );
}
