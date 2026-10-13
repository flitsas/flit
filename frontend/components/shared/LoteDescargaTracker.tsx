'use client';

import { createContext, useContext, useEffect, useMemo, useRef, type ReactNode } from 'react';
import {
  useConsolidadoLoteActual,
  type UseConsolidadoLoteActual,
} from '@/hooks/useConsolidadoLoteActual';
import { useLoteDescargaMinimizado } from '@/hooks/useLoteDescargaMinimizado';
import { useNombreHijaLote } from '@/hooks/useNombreHijaLote';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import {
  LoteDescargaAlertCard,
  TITULO_DESCARGA_MASIVA,
  IconoLote,
  loteActivo,
  porcentajeLote,
  textoEstadoLote,
  toneLoteConsolidados,
} from './LoteDescargaAlertCard';

/**
 * HU #13382 (épica #13216) — seguimiento global del lote de descarga masiva, montado por el `Shell`
 * para que el avance se vea en cualquier ruta (AC1).
 *
 * <p>`LoteDescargaTrackerProvider` corre `useConsolidadoLoteActual` una sola vez por Shell y publica
 * dos contextos: las acciones (estables: no re-renderizan las páginas en cada consulta de 4 s) y el
 * estado (solo lo lee el aviso). `useMostrarLoteDescarga()` es el punto de enganche para pedir que
 * se muestre un lote concreto: el listado al crear el lote o al recibir 409 `lote_activo` (#13381) y,
 * más adelante, la bandeja del OT (#13393/#13394). Fuera del provider es un no-op.</p>
 *
 * <p>`LoteDescargaTracker` pinta el aviso flotando arriba a la derecha del área de contenido del Shell:
 * no ocupa la barra superior (logo y nombre de la marca blanca, #12237), ni el dock (abajo al centro),
 * ni el botón de Dr. FLIT (abajo a la derecha).</p>
 *
 * Uso de ejemplo:
 *   <LoteDescargaTrackerProvider habilitado={puedeDescargaMasiva}>
 *     <SuiteShell overlay={<LoteDescargaTracker />}>{pagina}</SuiteShell>
 *   </LoteDescargaTrackerProvider>
 *   // en una página: const { mostrarLote } = useMostrarLoteDescarga(); mostrarLote(loteId);
 */

export interface AccionesLoteDescarga {
  /** Muestra y sigue un lote concreto (el objeto ya leído o su id). */
  mostrarLote: (lote: LoteConsolidados | string) => void;
}

const SIN_PROVIDER: AccionesLoteDescarga = { mostrarLote: () => undefined };

const AccionesCtx = createContext<AccionesLoteDescarga>(SIN_PROVIDER);
const EstadoCtx = createContext<UseConsolidadoLoteActual | null>(null);

export function useMostrarLoteDescarga(): AccionesLoteDescarga {
  return useContext(AccionesCtx);
}

export function LoteDescargaTrackerProvider({
  habilitado,
  children,
}: {
  /** Solo con `consolidado-masivo.download` (o SuperAdmin): sin permiso no hay polling. */
  habilitado: boolean;
  children: ReactNode;
}) {
  const seguimiento = useConsolidadoLoteActual({ habilitado });
  const { mostrarLote } = seguimiento;
  const acciones = useMemo<AccionesLoteDescarga>(
    () => (habilitado ? { mostrarLote } : SIN_PROVIDER),
    [habilitado, mostrarLote],
  );
  return (
    <AccionesCtx.Provider value={acciones}>
      <EstadoCtx.Provider value={habilitado ? seguimiento : null}>{children}</EstadoCtx.Provider>
    </AccionesCtx.Provider>
  );
}

/**
 * Aviso flotante del lote. Sin lote (204), oculto o sin permiso no pinta nada (AC5). HU #13419 AC6 —
 * el nombre de la hija de un lote de red acotado se resuelve aquí por `alcanceHijaId`, así que vale
 * igual para el lote recién creado que para el recuperado por `/actual` tras una recarga.
 *
 * <p>Minimizable: el botón «Minimizar la descarga masiva» desliza la tarjeta a la derecha y deja una
 * pestaña (40×56 px) pegada al borde derecho, a la altura de la tarjeta, con el icono y el % en el
 * tono del estado. La tarjeta sigue montada (inerte) para que su región viva anuncie los cambios.
 * El estado se recuerda por lote (`useLoteDescargaMinimizado`); si el lote termina minimizado sigue
 * minimizado y la pestaña cambia de tono/icono con un punto de aviso (opción a del usuario).</p>
 */
export function LoteDescargaTracker() {
  const s = useContext(EstadoCtx);
  const nombreHija = useNombreHijaLote(s?.oculto ? null : s?.lote);
  const [minimizado, setMinimizado] = useLoteDescargaMinimizado(s?.lote?.id ?? null);
  const pestanaRef = useRef<HTMLButtonElement>(null);
  const botonMinimizarRef = useRef<HTMLButtonElement>(null);
  // Solo un cambio hecho por la persona mueve el foco; el estado recordado al recargar no lo roba.
  const focoPendiente = useRef<'pestana' | 'tarjeta' | null>(null);

  useEffect(() => {
    const destino = focoPendiente.current;
    if (!destino) return;
    focoPendiente.current = null;
    (destino === 'pestana' ? pestanaRef : botonMinimizarRef).current?.focus();
  }, [minimizado]);

  if (!s?.lote || s.oculto) return null;
  const tone = toneLoteConsolidados(s.lote, s.expirado);
  const estado = textoEstadoLote(s.lote, s.expirado);
  const porcentaje = porcentajeLote(s.lote, s.expirado);
  const terminado = !loteActivo(s.lote, s.expirado);

  const cambiar = (minimizar: boolean) => {
    focoPendiente.current = minimizar ? 'pestana' : 'tarjeta';
    setMinimizado(minimizar);
  };

  return (
    // `overflow-x-clip`: la tarjeta que sale por la derecha no abre scroll horizontal en el contenido.
    <div className="pointer-events-none absolute inset-x-0 top-0 z-30 flex justify-end overflow-x-clip px-4 pt-4 md:px-6">
      <LoteDescargaAlertCard
        className="pointer-events-auto w-full max-w-xs"
        lote={s.lote}
        expirado={s.expirado}
        errorConsulta={s.errorConsulta}
        descargandoParte={s.descargandoParte}
        errorDescarga={s.errorDescarga}
        onDescargarParte={(n) => void s.descargarParte(n)}
        onCancelar={() => void s.cancelar()}
        cancelando={s.cancelando}
        errorCancelacion={s.errorCancelacion}
        nombreHija={nombreHija}
        onMinimizar={() => cambiar(true)}
        minimizado={minimizado}
        botonMinimizarRef={botonMinimizarRef}
      />
      {minimizado ? (
        <button
          ref={pestanaRef}
          type="button"
          data-testid="lote-descarga-pestana"
          data-tone={tone}
          onClick={() => cambiar(false)}
          aria-label={`Mostrar la descarga masiva — ${estado}${porcentaje !== null ? `, ${porcentaje} %` : ''}`}
          aria-expanded={false}
          title={TITULO_DESCARGA_MASIVA}
          className="pointer-events-auto absolute right-0 top-4 flex h-14 w-10 flex-col items-center justify-center gap-0.5 rounded-l-xl border border-r-0 bg-card text-xs font-semibold tabular-nums shadow-[0_8px_24px_rgba(22,39,68,0.08)] transition hover:brightness-95 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand motion-safe:animate-in motion-safe:fade-in motion-safe:slide-in-from-right motion-safe:duration-200"
          style={{
            backgroundImage: `linear-gradient(var(--badge-${tone}-bg), var(--badge-${tone}-bg))`,
            borderColor: `var(--badge-${tone}-border)`,
            color: `var(--badge-${tone}-fg)`,
          }}
        >
          {terminado ? (
            <span
              data-testid="lote-descarga-pestana-aviso"
              aria-hidden="true"
              className="absolute left-1 top-1 h-2 w-2 rounded-full ring-2 ring-card"
              style={{ background: `var(--badge-${tone}-fg)` }}
            />
          ) : null}
          <IconoLote tone={tone} className="h-4 w-4 shrink-0" />
          {porcentaje !== null ? <span aria-hidden="true">{porcentaje}%</span> : null}
        </button>
      ) : null}
    </div>
  );
}
