'use client';

import { createContext, useContext, type ReactNode } from 'react';

/**
 * Track C — modo solo lectura del wizard. Un trámite que ya salió del estado
 * `draft` (enviado a tránsito, en revisión, completado, rechazado) es solo
 * visualización: se puede recorrer pero no editar. En vez de propagar un flag
 * por props a toda la jerarquía de pasos, se expone vía contexto y cada
 * componente de captura/acción consume `useWizardReadOnly()` para deshabilitar
 * sus mutaciones. El backend ya protege con 409 `not_draft`; esto es la capa UX.
 */
const WizardReadOnlyContext = createContext(false);

export function WizardReadOnlyProvider({
  readOnly,
  children,
}: {
  readOnly: boolean;
  children: ReactNode;
}) {
  return (
    <WizardReadOnlyContext.Provider value={readOnly}>
      {children}
    </WizardReadOnlyContext.Provider>
  );
}

/** `true` si el wizard está en modo solo lectura. Por defecto editable. */
export function useWizardReadOnly(): boolean {
  return useContext(WizardReadOnlyContext);
}
