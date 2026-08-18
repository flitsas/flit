'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { tramitesClient } from '@/lib/api/tramites-client';
import { CarLoaderModal } from '@/components/atom/CarLoader';

/**
 * `/tramites/nuevo` — entrada al asistente SIN modalidad en la URL.
 *
 * En la propuesta "Nuevo trámite" abre el asistente directamente y el tipo se elige DENTRO del
 * paso 1, con las tarjetas de "Configuración del Trámite". No hay pantalla intermedia de elección,
 * así que esta ruta no pinta nada: resuelve qué modalidad abrir y reemplaza la URL por
 * `/tramites/nuevo/[modalidad]`, que es donde vive el paso 1.
 *
 * La modalidad por defecto es matrícula inicial —la primera tarjeta del selector—, salvo que la
 * compañía la tenga bloqueada: en ese caso abre la primera que sí esté habilitada, para no llevar
 * al gestor a una pantalla de bloqueo que él no eligió. Si las dos están bloqueadas se abre
 * igualmente la de matrícula, que es la que explica el bloqueo y ofrece volver al listado.
 *
 * `replace`, no `push`: esta URL es solo un enrutamiento y el "atrás" del navegador debe volver al
 * listado, no rebotar aquí otra vez.
 */
export default function NuevoTramitePage() {
  const router = useRouter();

  useEffect(() => {
    let active = true;
    const abrir = (modalidad: string) => {
      if (active) router.replace(`/tramites/nuevo/${modalidad}`);
    };
    void tramitesClient
      .getConsultationConfig()
      .then((cfg) => {
        const block = cfg.blockProcedureFamily;
        abrir(block?.matriculas && !block?.traspaso ? 'traspaso' : 'matricula_inicial');
      })
      .catch(() => {
        // Sin config legible no se decide nada: el paso 1 y el backend cortan si aplica.
        abrir('matricula_inicial');
      });
    return () => {
      active = false;
    };
  }, [router]);

  // La espera se pinta con el MISMO velo que las consultas al RUNT (`CarLoaderModal`): tarjeta
  // blanca con el carrito sobre el fondo atenuado. Antes era un rótulo suelto, que en una ruta que
  // no dibuja nada más dejaba la pantalla prácticamente en blanco.
  return <CarLoaderModal label="Abriendo el asistente…" />;
}
