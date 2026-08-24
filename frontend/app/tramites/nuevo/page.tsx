'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { tramitesClient } from '@/lib/api/tramites-client';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import { SelectorTipoTramite, type FamiliasBloqueadas } from '@/components/operacion/SelectorTipoTramite';

/**
 * `/tramites/nuevo` — elección del trámite antes de abrir el asistente (ADR-0050).
 *
 * Antes esta ruta no pintaba nada: resolvía una de las dos modalidades fijas y redirigía a
 * `/tramites/nuevo/[modalidad]`. Con el catálogo como fuente de verdad hay 21 tipos en tres
 * familias, así que la elección **familia → tipo** pasa a ser una pantalla propia y la URL del
 * asistente lleva el `code` del tipo.
 *
 * Solo se ofrecen los tipos con la barrera de operación encendida, de modo que el gestor nunca
 * elige un trámite sin recorrido, documentos ni causales.
 */
export default function NuevoTramitePage() {
  const router = useRouter();
  const [bloqueadas, setBloqueadas] = useState<FamiliasBloqueadas | undefined>(undefined);
  const [cargandoConfig, setCargandoConfig] = useState(true);

  useEffect(() => {
    let active = true;
    void tramitesClient
      .getConsultationConfig()
      .then((cfg) => {
        if (active) setBloqueadas(cfg.blockProcedureFamily ?? undefined);
      })
      .catch(() => {
        // Sin config legible no se bloquea nada por adelantado: el gate del backend corta al crear.
        if (active) setBloqueadas(undefined);
      })
      .finally(() => {
        if (active) setCargandoConfig(false);
      });
    return () => {
      active = false;
    };
  }, []);

  if (cargandoConfig) {
    return <CarLoaderModal label="Abriendo el asistente…" />;
  }

  return (
    <main className="mx-auto w-full max-w-xl px-4 py-8">
      <h1 className="text-lg font-semibold" style={{ color: '#162744' }}>Nuevo trámite</h1>
      <p className="mt-1 text-sm opacity-70">Elige la familia y luego el trámite que vas a radicar.</p>

      <div className="mt-5">
        <SelectorTipoTramite
          bloqueadas={bloqueadas}
          onElegir={(code) => router.push(`/tramites/nuevo/${encodeURIComponent(code)}`)}
        />
      </div>

      <button
        type="button"
        onClick={() => router.push('/tramites')}
        className="mt-6 text-xs font-semibold underline focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
      >
        Volver a trámites
      </button>
    </main>
  );
}
