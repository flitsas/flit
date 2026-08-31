'use client';

import { useState } from 'react';
import { createPortal } from 'react-dom';
import { Car } from 'lucide-react';

/**
 * Loader de espera larga del asistente de trámites, portado de `CarLoader.tsx` de la propuesta.
 *
 * Existe porque las tres esperas del flujo —consultar el RUNT, analizar documentos con OCR y
 * radicar el expediente— duran segundos y no milisegundos, y hasta ahora solo se señalaban
 * cambiando el rótulo del botón a «Consultando…». Con el botón fuera de la vista, o con la
 * atención en otra parte de la pantalla, no había ninguna señal de que el sistema estuviera
 * trabajando.
 *
 * El mensaje va por modo y no es decorativo: dice ANTE QUIÉN se está esperando. Que el RUNT tarde
 * es un hecho del dominio, y nombrarlo evita que la demora se lea como un cuelgue de la aplicación.
 */
export type CarLoaderMode = 'runt' | 'ocr' | 'radicacion';

const MENSAJES: Record<CarLoaderMode, string> = {
  runt: 'Consultando información en el RUNT…',
  // Sin «IA» ni «OCR»: las dos son jerga de cómo está hecho el sistema, no de lo que el gestor está
  // esperando. Lo que ocurre es que se están LEYENDO los documentos que acaba de cargar, y eso es lo
  // que anticipa la pantalla que viene después (el reparto por casillas, que él revisa).
  ocr: 'Leyendo los documentos que cargaste…',
  radicacion: 'Radicando expediente ante el organismo de tránsito…',
};

const BASE = '/assets/loading';

/**
 * Escena de tres capas: la pista fija, la línea en órbita antihoraria y el vehículo girando a
 * favor. Si alguno de los SVG no carga, cae a una órbita dibujada con bordes y el icono de
 * vehículo — la espera se sigue comunicando aunque falten los recursos estáticos.
 */
export function CarLoader({ mode = 'runt', label }: { mode?: CarLoaderMode; label?: string }) {
  const [sinRecursos, setSinRecursos] = useState(false);
  const mensaje = label ?? MENSAJES[mode];

  return (
    <div
      className="flex flex-col items-center justify-center gap-3"
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      {/* `<img>` y no `next/image`: son tres SVG decorativos que suman menos de 3 KB, se sirven
          desde /public sin transformación posible, y aquí lo que importa es el `onError` — la
          espera tiene que seguir comunicándose aunque los recursos no estén. El optimizador no
          aporta nada a eso y sí complica el fallback. */}
      {/* eslint-disable @next/next/no-img-element */}
      <div className="relative flex h-32 w-32 items-center justify-center">
        {sinRecursos ? (
          <div className="flit-loader">
            <div className="flit-vehicle">
              <Car className="h-8 w-8" aria-hidden="true" />
            </div>
          </div>
        ) : (
          <>
            {/* Capa 1 — la pista, fija. */}
            <img
              src={`${BASE}/circulo.svg`}
              alt=""
              aria-hidden="true"
              className="pointer-events-none absolute inset-0 h-full w-full object-contain"
              onError={() => setSinRecursos(true)}
            />
            {/* Capa 2 — la órbita exterior, en sentido antihorario. */}
            <div className="flit-escena-orbita absolute inset-0">
              <img
                src={`${BASE}/linea.svg`}
                alt=""
                aria-hidden="true"
                className="pointer-events-none absolute left-1/2 top-[5%] h-auto w-20 -translate-x-1/2 object-contain"
                onError={() => setSinRecursos(true)}
              />
            </div>
            {/* Capa 3 — el vehículo sobre la pista, en sentido horario. */}
            <div className="flit-escena-vehiculo absolute inset-0">
              <img
                src={`${BASE}/carro.svg`}
                alt=""
                aria-hidden="true"
                className="pointer-events-none absolute left-1/2 top-[10%] h-auto w-10 -translate-x-1/2 object-contain"
                onError={() => setSinRecursos(true)}
              />
            </div>
          </>
        )}
      </div>
      {/* eslint-enable @next/next/no-img-element */}

      {/* El mensaje ES el título. Antes iba «Cargando…» en negrita y el mensaje debajo, en pequeño y
          atenuado: el rótulo genérico se llevaba toda la jerarquía sin decir nada, y lo único que
          informa —ante quién se espera— quedaba de subtítulo de una palabra vacía. */}
      <div className="text-center">
        <p className="max-w-xs text-sm font-semibold">{mensaje}</p>
      </div>
    </div>
  );
}

/**
 * La misma escena sobre un velo que cubre la pantalla.
 *
 * Se monta solo mientras dura la espera y NO atrapa el foco: es un aviso, no un diálogo, y
 * secuestrar el foco para devolverlo un segundo después desorienta más de lo que ayuda. El velo sí
 * bloquea el puntero, que es lo que se busca — evitar el doble envío mientras el proveedor responde.
 */
export function CarLoaderModal({ mode = 'runt', label }: { mode?: CarLoaderMode; label?: string }) {
  // Portal a <body>, igual que `Modal` y por el mismo motivo: un `fixed inset-0` renderizado dentro
  // del árbol del asistente resuelve su posición contra el primer ancestro que cree un containing
  // block —basta un `transform`, un `filter` o un `backdrop-filter`—, y el velo quedaba recortado
  // al recuadro del formulario en vez de cubrir la pantalla. Es exactamente lo que pasaba al
  // consultar desde el paso de actores: el velo se montaba, pero no se veía.
  if (typeof document === 'undefined') return null;

  return createPortal(
    // Overlay del token (`component.modal.overlay` + `overlayBlur`), que es uno de los dos únicos
    // desenfoques autorizados del sistema. La propuesta usa aquí un velo claro y una caja
    // semitransparente con blur; esa caja sería cristal fuera de las dos excepciones, así que el
    // desenfoque se queda en el velo y el contenedor va opaco.
    <div
      className="fixed inset-0 z-[1200] flex items-center justify-center p-6 backdrop-blur-[6px]"
      style={{ background: 'rgba(22, 39, 68, 0.45)' }}
    >
      <div
        className="rounded-2xl border border-[#DFE5ED] bg-white p-8 dark:border-white/10 dark:bg-[#162744]"
        style={{ boxShadow: '0 24px 60px rgba(22, 39, 68, 0.18)' }}
      >
        <CarLoader mode={mode} label={label} />
      </div>
    </div>,
    document.body,
  );
}
