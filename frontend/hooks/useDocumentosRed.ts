'use client';

import { useEffect, useState } from 'react';
import { fetchNetworkDocumentos } from '@/lib/api/tramites-client';

/**
 * `inactivo`: la vista de red no está activa (no se consulta nada). `consultando`: aún sin respuesta.
 * `habilitado`: `documentosRed=true`. `apagado`: `documentosRed=false`. `no_disponible`: la consulta
 * falló (403, 5xx o red) — fail-closed: tampoco se ofrece la descarga.
 */
export type EstadoDocumentosRed = 'inactivo' | 'consultando' | 'habilitado' | 'apagado' | 'no_disponible';

type Resultado = Exclude<EstadoDocumentosRed, 'inactivo' | 'consultando'>;

/**
 * HU #13419 AC5 (épica #13216) — ¿la cabeza puede descargar documentos de la vista de red?
 *
 * Consulta `GET /api/v1/tramites/network/documentos` UNA vez cada vez que la vista de red se activa
 * (pasar de «toda la red» a una hija no la repite), con `AbortSignal` para no escribir estado tras
 * desactivarla o desmontar. Hasta que responde `true` el llamador no debe ofrecer «Descargar ZIP».
 *
 * Uso de ejemplo:
 *   const docsRed = useDocumentosRed(networkActive && puedeLote);
 *   if (docsRed === 'habilitado') <BotonDescargaMasivaZip … />
 */
export function useDocumentosRed(activo: boolean): EstadoDocumentosRed {
  const [resultado, setResultado] = useState<Resultado | null>(null);
  // Al (des)activar la red se descarta el resultado anterior durante el render (patrón «ajustar estado
  // al cambiar una prop»): cada activación vuelve a `consultando`, sin setState síncrono en el efecto.
  const [activoPrevio, setActivoPrevio] = useState(activo);
  if (activo !== activoPrevio) {
    setActivoPrevio(activo);
    setResultado(null);
  }

  useEffect(() => {
    if (!activo) return;
    const ctrl = new AbortController();
    fetchNetworkDocumentos(ctrl.signal).then(
      (habilitado) => {
        if (!ctrl.signal.aborted) setResultado(habilitado ? 'habilitado' : 'apagado');
      },
      () => {
        if (!ctrl.signal.aborted) setResultado('no_disponible');
      },
    );
    return () => ctrl.abort();
  }, [activo]);

  if (!activo) return 'inactivo';
  return resultado ?? 'consultando';
}
