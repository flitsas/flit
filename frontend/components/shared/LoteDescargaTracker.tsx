'use client';

import { createContext, useContext, useMemo, type ReactNode } from 'react';
import {
  useConsolidadoLoteActual,
  type UseConsolidadoLoteActual,
} from '@/hooks/useConsolidadoLoteActual';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';
import { LoteDescargaAlertCard } from './LoteDescargaAlertCard';

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

/** Aviso flotante del lote. Sin lote (204), oculto o sin permiso no pinta nada (AC5). */
export function LoteDescargaTracker() {
  const s = useContext(EstadoCtx);
  if (!s?.lote || s.oculto) return null;
  return (
    <div className="pointer-events-none absolute inset-x-0 top-0 z-30 flex justify-end px-4 pt-4 md:px-6">
      <LoteDescargaAlertCard
        className="pointer-events-auto w-full max-w-sm"
        lote={s.lote}
        expirado={s.expirado}
        errorConsulta={s.errorConsulta}
        descargandoParte={s.descargandoParte}
        errorDescarga={s.errorDescarga}
        onDescargarParte={(n) => void s.descargarParte(n)}
        onCancelar={() => void s.cancelar()}
        cancelando={s.cancelando}
        errorCancelacion={s.errorCancelacion}
      />
    </div>
  );
}
