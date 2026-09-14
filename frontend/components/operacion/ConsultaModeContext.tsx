'use client';

import { createContext, useContext, useMemo, type ReactNode } from 'react';
import type { ProcedureInstanceDetail } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12362 — modo consulta del detalle de un trámite de la red.
 *
 * Es el gemelo de `WizardReadOnlyContext` para el modal de detalle: las secciones (`detalle/*`)
 * lo leen para no ofrecer ninguna gestión de documentos y para convertir un 403/404 de alcance en
 * el copy del AC3 en vez de un error técnico. Por defecto `false`: un trámite propio o un cliente
 * sin jerarquía no notan que existe (AC4/AC5).
 *
 * Además del booleano, el contexto transporta el DETALLE CONSOLIDADO que el modal ya pidió por
 * `GET /tramites/network/instances/{id}` (`actors`, `fieldValues`, …): en consulta las secciones
 * que hoy tienen ruta propia (actores) leen de aquí en vez de disparar peticiones que el servidor
 * rechaza por diseño (404 anti-enumeración). Fuera de consulta `detalle` es `null` y cada sección
 * sigue con sus llamadas de siempre.
 */
export interface ConsultaModeValue {
  consultaMode: boolean;
  /** Detalle consolidado del trámite (solo en consulta; `null` mientras carga o fuera de consulta). */
  detalle: ProcedureInstanceDetail | null;
  /** `true` mientras el modal está pidiendo el detalle consolidado. */
  detalleLoading: boolean;
  /** Error (ya descrito) de la carga del detalle consolidado, si la hubo. */
  detalleError: string | null;
  /** Reintenta la carga del detalle consolidado (solo tiene efecto en consulta). */
  reintentarDetalle: () => void;
}

const VALOR_POR_DEFECTO: ConsultaModeValue = {
  consultaMode: false,
  detalle: null,
  detalleLoading: false,
  detalleError: null,
  reintentarDetalle: () => {},
};

const ConsultaModeContext = createContext<ConsultaModeValue>(VALOR_POR_DEFECTO);

export function ConsultaModeProvider({
  consultaMode,
  detalle = null,
  detalleLoading = false,
  detalleError = null,
  reintentarDetalle,
  children,
}: {
  consultaMode: boolean;
  detalle?: ProcedureInstanceDetail | null;
  detalleLoading?: boolean;
  detalleError?: string | null;
  reintentarDetalle?: () => void;
  children: ReactNode;
}) {
  const value = useMemo<ConsultaModeValue>(
    () => ({
      consultaMode,
      // Fuera de consulta el detalle no viaja: las secciones no deben cambiar de fuente (AC4/AC5).
      detalle: consultaMode ? detalle : null,
      detalleLoading: consultaMode ? detalleLoading : false,
      detalleError: consultaMode ? detalleError : null,
      reintentarDetalle: reintentarDetalle ?? VALOR_POR_DEFECTO.reintentarDetalle,
    }),
    [consultaMode, detalle, detalleLoading, detalleError, reintentarDetalle],
  );
  return <ConsultaModeContext.Provider value={value}>{children}</ConsultaModeContext.Provider>;
}

/** `true` si el detalle está en modo consulta (trámite de un cliente hijo). */
export function useConsultaMode(): boolean {
  return useContext(ConsultaModeContext).consultaMode;
}

/** Detalle consolidado del trámite en consulta (ver `ConsultaModeValue`). */
export function useDetalleConsolidado(): ConsultaModeValue {
  return useContext(ConsultaModeContext);
}
