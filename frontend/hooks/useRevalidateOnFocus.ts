'use client';

import { useEffect } from 'react';

/**
 * Vuelve a pedir datos del servidor cuando la pestaña recupera el foco.
 *
 * El caso que lo motiva: el gestor está parado en el paso de requisitos, se va al módulo Documental
 * —otra pestaña— y da de alta un documento OBLIGATORIO para ese trámite. Al volver, la pantalla
 * seguía con la lista con la que se montó: el documento nuevo no tenía casilla donde cargarlo y
 * tampoco frenaba el paso. Al reabrir o reanudar el trámite aparecía, porque eso sí vuelve a montar
 * los hooks. El dato del servidor estaba bien; lo que faltaba era pedirlo otra vez.
 *
 * Se escuchan los DOS eventos a propósito: `visibilitychange` cubre el cambio de pestaña dentro de
 * la misma ventana y `focus` el cambio de ventana (o volver desde otra aplicación), que en varios
 * navegadores no dispara el primero. Ambos se filtran por `visibilityState` para no revalidar contra
 * una pestaña que sigue oculta.
 *
 * `revalidate` debe ser SILENCIOSA —sin poner la vista en «cargando» y sin borrar lo que ya está en
 * pantalla si falla—: se dispara sin que el gestor la pida, y un parpadeo o un error a media captura
 * es peor que el dato viejo que venía a corregir.
 */
export function useRevalidateOnFocus(revalidate: () => void, enabled = true) {
  useEffect(() => {
    if (!enabled) return;

    const alVolver = () => {
      if (document.visibilityState !== 'visible') return;
      revalidate();
    };

    window.addEventListener('focus', alVolver);
    document.addEventListener('visibilitychange', alVolver);
    return () => {
      window.removeEventListener('focus', alVolver);
      document.removeEventListener('visibilitychange', alVolver);
    };
  }, [revalidate, enabled]);
}
