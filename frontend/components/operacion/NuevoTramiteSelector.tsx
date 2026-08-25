'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  NuevoTramiteModalContent,
  type FamiliasBloqueadas,
} from './NuevoTramiteModalContent';

/**
 * Elección del trámite a crear, con la configuración de la compañía ya resuelta.
 *
 * Presentaciones: modal sobre `/tramites` y página `/tramites/nuevo`. Ambas montan el mismo
 * contenido mockup (`NuevoTramiteModalContent`) para no divergir.
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
    return <p className="text-xs opacity-70">Cargando tipos de trámite…</p>;
  }

  return (
    <NuevoTramiteModalContent
      bloqueadas={bloqueadas}
      onElegir={onElegir}
      onCancelar={onCancelar}
      tituloEnContenedor={tituloEnContenedor}
    />
  );
}
