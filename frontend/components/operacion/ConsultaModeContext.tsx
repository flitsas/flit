'use client';

import { createContext, useContext, type ReactNode } from 'react';

/**
 * HU #12362 — modo consulta del detalle de un trámite de la red.
 *
 * Es el gemelo de `WizardReadOnlyContext` para el modal de detalle: las secciones (`detalle/*`)
 * lo leen para no ofrecer ninguna gestión de documentos y para convertir un 403/404 de alcance en
 * el copy del AC3 en vez de un error técnico. Por defecto `false`: un trámite propio o un cliente
 * sin jerarquía no notan que existe (AC4/AC5).
 */
const ConsultaModeContext = createContext(false);

export function ConsultaModeProvider({
  consultaMode,
  children,
}: {
  consultaMode: boolean;
  children: ReactNode;
}) {
  return (
    <ConsultaModeContext.Provider value={consultaMode}>{children}</ConsultaModeContext.Provider>
  );
}

/** `true` si el detalle está en modo consulta (trámite de un cliente hijo). */
export function useConsultaMode(): boolean {
  return useContext(ConsultaModeContext);
}
