'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { SelectorTipoTramite, type FamiliasBloqueadas } from './SelectorTipoTramite';

/**
 * Elección del trámite a crear, con la configuración de la compañía ya resuelta.
 *
 * Existe para que el selector se pueda presentar de las DOS formas sin duplicar nada: como modal
 * sobre el listado —que es de donde el gestor lo lanza— y como página en `/tramites/nuevo`, que se
 * conserva para enlaces directos y para el botón atrás. Cargar aquí el bloqueo por familia evita
 * que cada presentación repita el mismo `getConsultationConfig`.
 */
export function NuevoTramiteSelector({
  onElegir,
  onCancelar,
  tituloEnContenedor = false,
}: {
  onElegir: (code: string) => void;
  onCancelar?: () => void;
  /** El contenedor ya pinta el título y el subtítulo (p. ej. la cabecera del modal). */
  tituloEnContenedor?: boolean;
}) {
  const [bloqueadas, setBloqueadas] = useState<FamiliasBloqueadas | undefined>(undefined);
  const [cargando, setCargando] = useState(true);

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
        if (active) setCargando(false);
      });
    return () => {
      active = false;
    };
  }, []);

  if (cargando) {
    return <p className="text-sm opacity-70">Cargando tipos de trámite…</p>;
  }

  return (
    <SelectorTipoTramite
      bloqueadas={bloqueadas}
      onElegir={onElegir}
      onCancelar={onCancelar}
      tituloEnContenedor={tituloEnContenedor}
    />
  );
}
