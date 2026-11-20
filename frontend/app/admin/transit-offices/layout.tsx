'use client';

import { type ReactNode } from 'react';
import { Shell } from '@/components/atom/Shell';
import { useAccessibleModules } from '@/hooks/useAccessibleModules';
import { useAuthGate } from '@/hooks/useAuthGate';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSoloCaja } from '@/lib/auth/jwt';

/**
 * Layout de /admin/transit-offices/*. Igual que el de /admin/companies: replica el chrome
 * de la SPA (auth real por JWT + Shell con dock) para que el dock NO desaparezca al entrar
 * a la consola de organismos de tránsito ni a sus subpantallas. El dock navega de vuelta
 * a la SPA (/?m=…) o a /tramites. Admin OT navega el hub desde el dock; SuperAdmin
 * conserva la píldora «Tránsito» hacia el listado de organismos.
 */
export default function AdminTransitOfficesLayout({ children }: { children: ReactNode }) {
  const { authed, hydrated, logout } = useAuthGate();

  // La persona de caja (solo `ot_caja`) no puede pedir el catálogo de módulos (403 `caja_sin_acceso`): su menú no depende de él.
  const soloCaja = hydrated && authed && isSoloCaja(decodeJwtPayload(getToken()));
  const { modules: accessibleModules, loading: modulesLoading } = useAccessibleModules(authed && !soloCaja);
  const accessibleCodes = accessibleModules.map((m) => m.code);

  if (!hydrated || !authed) return null;

  return (
    <Shell onLogout={logout} visibleModuleCodes={modulesLoading ? [] : accessibleCodes}>
      <div className="app-bg w-full">{children}</div>
    </Shell>
  );
}
