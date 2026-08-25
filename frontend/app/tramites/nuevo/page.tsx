'use client';

import { useRouter } from 'next/navigation';
import { NuevoTramiteSelector } from '@/components/operacion/NuevoTramiteSelector';

/**
 * `/tramites/nuevo` — elección del trámite antes de abrir el asistente (ADR-0050).
 *
 * La vía normal de llegar aquí es el modal sobre el listado (`/tramites`), que preserva filtros y
 * scroll al cancelar. Esta ruta se conserva para lo que un modal no puede dar: enlace directo,
 * marcador, abrir en pestaña nueva y botón atrás. Monta el MISMO componente que el modal, así que
 * las dos presentaciones no pueden divergir.
 */
export default function NuevoTramitePage() {
  const router = useRouter();

  return (
    // El ancho lo pide la rejilla: `max-w-xl` dejaba las tres familias apiladas incluso en
    // escritorio, que es justo lo que la composición de la propuesta resuelve poniéndolas en fila.
    <main className="mx-auto w-full max-w-3xl px-4 py-8">
      {/* El título de pantalla no flota sobre el fondo azul: va en tarjeta blanca, como el resto de
          la app interna. Aquí lo pinta el propio selector dentro de la tarjeta, en un solo bloque. */}
      <h1 className="sr-only">Nuevo trámite</h1>

      <div className="rounded-2xl border bg-white p-6 dark:bg-[#162744]" style={{ borderColor: '#DFE5ED' }}>
        <NuevoTramiteSelector
          onElegir={(code) => router.push(`/tramites/nuevo/${encodeURIComponent(code)}`)}
          onCancelar={() => router.push('/tramites')}
        />
      </div>
    </main>
  );
}
