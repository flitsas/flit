'use client';

import { type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { Shell, type ModuleId } from '@/components/atom/Shell';
import { useAccessibleModules } from '@/hooks/useAccessibleModules';
import { useAuthGate } from '@/hooks/useAuthGate';

/**
 * Layout de /admin/transit-offices/*. Igual que el de /admin/companies: replica el chrome
 * de la SPA (auth real por JWT + Shell con dock) para que el dock NO desaparezca al entrar
 * a la consola de organismos de tránsito ni a sus subpantallas. El dock navega de vuelta
 * a la SPA (/?m=…) o a /tramites. Admin OT navega el hub desde el dock; SuperAdmin
 * conserva la píldora «Tránsito» hacia el listado de organismos.
 */
export default function AdminTransitOfficesLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { authed, hydrated, logout } = useAuthGate();

  const { modules: accessibleModules, loading: modulesLoading } = useAccessibleModules(authed);
  const accessibleCodes = accessibleModules.map((m) => m.code);

  const handleNav = (m: ModuleId) => {
    if (m === 'tramites') router.push('/tramites');
    else router.push(`/?m=${m}`);
  };

  if (!hydrated || !authed) return null;

  return (
    <Shell active="dashboard" onNav={handleNav} onLogout={logout} visibleModuleCodes={modulesLoading ? [] : accessibleCodes}>
      <div className="app-bg min-h-screen w-full">{children}</div>
    </Shell>
  );
}
