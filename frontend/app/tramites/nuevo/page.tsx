'use client';

import { useRouter } from 'next/navigation';
import { NuevoTramiteSelector } from '@/components/operacion/NuevoTramiteSelector';

/**
 * `/tramites/nuevo` — elección del trámite antes de abrir el asistente.
 *
 * La vía normal es el modal sobre el listado (`/tramites`). Esta ruta se conserva para enlace
 * directo, marcador y botón atrás. Monta el mismo selector mockup que el modal.
 */
export default function NuevoTramitePage() {
  const router = useRouter();

  return (
    <main className="mx-auto w-full max-w-4xl px-4 py-8">
      <h1 className="sr-only">Nuevo trámite</h1>

      <div
        className="rounded-2xl border bg-white p-7 dark:bg-[#162744]"
        style={{ borderColor: '#DFE5ED' }}
      >
        <NuevoTramiteSelector
          onElegir={(code) => router.push(`/tramites/nuevo/${encodeURIComponent(code)}`)}
          onCancelar={() => router.push('/tramites')}
        />
      </div>
    </main>
  );
}
