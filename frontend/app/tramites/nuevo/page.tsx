'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useFamilyAccess } from '@/hooks/useFamilyAccess';
import { accessDeniedPath } from '@/lib/auth/guard';
import { NuevoTramiteSelector } from '@/components/operacion/NuevoTramiteSelector';

/**
 * `/tramites/nuevo` — elección del trámite antes de abrir el asistente.
 *
 * La vía normal es el modal sobre el listado (`/tramites`). Esta ruta se conserva para enlace
 * directo, marcador y botón atrás. Monta el mismo selector mockup que el modal.
 */
export default function NuevoTramitePage() {
  const router = useRouter();
  // HU #13665 — sin Crear en ninguna familia no hay nada que elegir: al inicio con el aviso de acceso denegado.
  const { creatable } = useFamilyAccess();
  const sinAcceso = creatable.length === 0;
  useEffect(() => {
    if (sinAcceso) router.replace(accessDeniedPath('tramites'));
  }, [sinAcceso, router]);

  if (sinAcceso) return null;

  return (
    <main className="mx-auto w-full max-w-4xl px-4 py-8">
      <h1 className="sr-only">Nuevo trámite</h1>

      <div
        className="rounded-2xl border bg-white p-7 dark:bg-flit-primary"
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
